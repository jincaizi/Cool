# 属性剑气 01：共享表、权威换装、普攻参数与剑气快照 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [x]`）语法来跟踪进度。

**目标** 建立共享配置、权威换装和普攻参数闭环。两个客户端从正常登录进入，能在背包切换武器，用权威施法确认和发射/停点消息识别同一道剑气。

**架构：** 共享纯 C# DTO 放在 `SyncCore/Elemental/`，服务端与客户端读同一份 JSON；服务端 `PlayerState` 持有武器实例与装备版本，`EquipHandler` 校验换装，`CastHandler` 在 `Decide` 冻结 `WeaponCastSnapshot` 并分配单调 `CastId`；客户端 `ClientEquipmentState` 按版本应用确认，`RemoteCastPresenter` 按 `CastId` 跟踪剑气，Animator 用攻击 state 的 `AttackSpeed` 参数承载攻速。

**技术栈：** Unity 2022.3.25f1、C#、Protobuf-net、xUnit、Unity EditMode/PlayMode、KCP/WebSocket。

---

## 范围与非目标

**本计划交付：**
- `ElementalCatalogDefinition`、校验器、默认表（默认剑 + 冰/火/雷/风模板）。
- 装备请求/确认协议；服务端权威武器槽与版本。
- `CastId` 分配、`CastBroadcast` 的攻速/元素/装备版本字段。
- 客户端装备确认应用、武器表现切换入口。
- 攻击 Animator `AttackSpeed` 参数（本地 + 远端）。
- 联机发射和停点统一按服务端 `CastId` 路由，本人和旁观者消费同一份描述。
- 服务端场景标识用于隔离联机剑气候选及收件人。Plan 02 在此基础上补齐 AOI 重入和在途剑气基线。

**本计划不做：** 元素状态结算、DoT、`CastCancelBroadcast`、`CombatStateUpdate`/`CombatProjection`、B5 位移预算、PvP HP/控制规则、三客户端验收。这些在 02–05 计划。

## 文件结构

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefinition.cs` | 共享 DTO：武器/元素/状态/普攻时序/剑气几何/PvP 参数 |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogValidator.cs` | 纯函数校验，服务端构建与客户端加载共用 |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefaults.cs` | 首版默认表与稳定 ID |
| `Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/WeaponCastSnapshot.cs` | 施法时冻结的武器参数值类型 |
| `Assets/Scripts/AOT/Protocol/Messages/Equipment.cs` | `EquipWeaponRequest` / `EquipWeaponConfirm` |
| `Assets/Scripts/AOT/Protocol/Messages/Combat.cs` | `CastRequest` / `CastBroadcast` / `HitEntry` 新字段 |
| `Assets/Scripts/AOT/Protocol/MessageId.cs`、`MessageCodec.cs`、`Messages/Login.cs` | 联机消息 ID、显式注册、能力和配置指纹、登录装备基线 |
| `Server/Player/PlayerState.cs` | 武器实例、装备版本、`LifeId` |
| `Server/Handler/EquipHandler.cs` | 换装校验与确认 |
| `Server/Handler/CastHandler.cs` | `CastId` 分配、`WeaponCastSnapshot` 冻结 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientEquipmentState.cs` | 客户端按版本应用装备确认 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs` | 路由 `EquipWeaponConfirm` |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs` | 现有武器表现切换入口 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimHashes.cs` | 新增 `AttackSpeed` 参数哈希 |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs` | 远端玩家攻击 speed |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs` | 按 `CastId` 跟踪与攻速写入 |

---

## 任务 1：共享元素配置 DTO、校验器与默认表

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefinition.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogValidator.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefaults.cs`
- 测试：`Server/Server.Tests/Elemental/ElementalCatalogValidatorTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncCore/ElementalCatalogTests.cs`

- [x] **步骤 1：编写失败的服务端测试**

```csharp
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class ElementalCatalogValidatorTests
{
    [Fact]
    public void Default_Catalog_Is_Valid()
    {
        var catalog = ElementalCatalogDefaults.Create();
        Assert.Empty(ElementalCatalogValidator.Validate(catalog));
        Assert.Contains(catalog.Weapons, w => w.TemplateId == ElementalCatalogDefaults.DefaultSwordTemplateId);
        Assert.Contains(catalog.Weapons, w => w.ElementId != 0);
    }

    [Fact]
    public void Rejects_Zero_BaseAttack()
    {
        var catalog = ElementalCatalogDefaults.Create();
        catalog.Weapons[0].BaseAttack = 0f;
        Assert.Contains("weapon stats out of range", ElementalCatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Rejects_Duplicate_Status_Group_And_Bad_Duration()
    {
        var catalog = ElementalCatalogDefaults.Create();
        catalog.Statuses.Add(new StatusEffectDefinition
        {
            EffectId = catalog.Statuses[0].EffectId,
            EffectKind = 1,
            StackGroupId = 1,
            DurationSeconds = 0f
        });
        var errors = ElementalCatalogValidator.Validate(catalog);
        Assert.Contains("status duplicate/invalid id", errors);
        Assert.Contains("status duration out of range", errors);
    }
}
```

- [x] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~ElementalCatalogValidatorTests"`
预期：编译失败，`ElementalCatalogDefaults` / `ElementalCatalogValidator` 不存在。若 testhost 抛 `Win32Exception(5)`，改用 `bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~ElementalCatalogValidatorTests"`。

- [x] **步骤 3：编写 DTO**

`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefinition.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace Cool.SyncCore
{
    [Serializable]
    public sealed class ElementalCatalogDefinition
    {
        public int SchemaVersion = 1;
        public string ContentHash = "";
        public int DefaultWeaponTemplateId;
        public List<int> StartingWeaponTemplateIds = new List<int>();
        public List<WeaponDefinition> Weapons = new List<WeaponDefinition>();
        public List<ElementDefinition> Elements = new List<ElementDefinition>();
        public List<StatusEffectDefinition> Statuses = new List<StatusEffectDefinition>();
        public List<AttackTimingDefinition> AttackTimings = new List<AttackTimingDefinition>();
        public ProjectileGeometryDefinition SwordQi = new ProjectileGeometryDefinition();
        public PvpCombatSettings Pvp = new PvpCombatSettings();
    }

    [Serializable]
    public sealed class WeaponDefinition
    {
        public int TemplateId;
        public string WeaponViewKey = "";
        public int ElementId;
        public float BaseAttack = 1f;
        public float AttackSpeedMultiplier = 1f;
        public float SwordQiDamageMultiplier = 1f;
    }

    [Serializable]
    public sealed class ElementDefinition
    {
        public int ElementId;
        public string SwordQiViewKey = "";
        public List<int> StatusEffectIds = new List<int>();
    }

    [Serializable]
    public sealed class StatusEffectDefinition
    {
        public int EffectId;
        public int EffectKind; // 1=Slow 2=Burn 3=Paralyze 4=Vulnerable
        public int StackGroupId;
        public float DurationSeconds;
        public float IntervalSeconds;
        public float DamagePerTick;
        public float SlowRatio;
        public float IncomingDamageMultiplier = 1f;
        public string ViewKey = "";
    }

    [Serializable]
    public sealed class AttackTimingDefinition
    {
        public int SkillId;
        public float WindupSeconds;
        public float ActiveSeconds;
        public float RecoverySeconds;
        public float ChainWindowSeconds;
        public float CooldownSeconds;
    }

    [Serializable]
    public sealed class ProjectileGeometryDefinition
    {
        public float Length = 12f;
        public float HalfWidth = 2f;
        public float Speed = 18f;
        public int Stages = 3;
        public float TotalFlightSeconds = 12f / 18f;
        public float SwordQiDamageCoefficient = 1f;
    }

    [Serializable]
    public sealed class PvpCombatSettings
    {
        public float PlayerMaxHp = 720f;
        public float WindVulnerabilityMultiplier = 1.28f;
        public float ControlImmunitySeconds = 0.5f;
        public float SameGroupVulnerabilityNoRefreshSeconds = 4f;
        public float ThunderDurationSeconds = 0.8f;
    }
}
```

- [x] **步骤 4：编写校验器**

`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogValidator.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace Cool.SyncCore
{
    public static class ElementalCatalogValidator
    {
        public static List<string> Validate(ElementalCatalogDefinition catalog)
        {
            var errors = new List<string>();
            if (catalog == null) { errors.Add("catalog missing"); return errors; }
            if (catalog.SchemaVersion != 1) errors.Add("unsupported SchemaVersion");

            var weaponIds = new HashSet<int>();
            foreach (var w in catalog.Weapons)
            {
                if (w == null) { errors.Add("weapon missing"); continue; }
                if (w.TemplateId <= 0 || !weaponIds.Add(w.TemplateId))
                    errors.Add("weapon duplicate/invalid id");
                if (string.IsNullOrWhiteSpace(w.WeaponViewKey))
                    errors.Add("weapon view key missing");
                if (!Positive(w.BaseAttack) || !InRange(w.AttackSpeedMultiplier, 0.25f, 4f)
                    || !Positive(w.SwordQiDamageMultiplier))
                    errors.Add("weapon stats out of range");
            }

            var elementIds = new HashSet<int>();
            foreach (var e in catalog.Elements)
            {
                if (e == null) { errors.Add("element missing"); continue; }
                if (e.ElementId <= 0 || !elementIds.Add(e.ElementId))
                    errors.Add("element duplicate/invalid id");
                if (string.IsNullOrWhiteSpace(e.SwordQiViewKey))
                    errors.Add("element view key missing");
            }

            var effectIds = new HashSet<int>();
            foreach (var s in catalog.Statuses)
            {
                if (s == null) { errors.Add("status missing"); continue; }
                if (s.EffectId <= 0 || !effectIds.Add(s.EffectId))
                    errors.Add("status duplicate/invalid id");
                if (s.EffectKind < 1 || s.EffectKind > 4)
                    errors.Add("status kind out of range");
                if (s.StackGroupId <= 0) errors.Add("status group invalid");
                if (!Positive(s.DurationSeconds) || s.DurationSeconds > 60f)
                    errors.Add("status duration out of range");
                if (s.EffectKind == 2 && (!Positive(s.IntervalSeconds) || s.IntervalSeconds > s.DurationSeconds))
                    errors.Add("status interval out of range");
                if (s.EffectKind == 1 && !InRange(s.SlowRatio, 0.01f, 0.99f))
                    errors.Add("status slow ratio out of range");
                if (s.EffectKind == 4 && (!Positive(s.IncomingDamageMultiplier) || s.IncomingDamageMultiplier < 1f))
                    errors.Add("status vulnerability out of range");
            }

            var timingIds = new HashSet<int>();
            foreach (var t in catalog.AttackTimings)
            {
                if (t == null) { errors.Add("timing missing"); continue; }
                if (t.SkillId <= 0 || !timingIds.Add(t.SkillId))
                    errors.Add("timing duplicate/invalid id");
                if (!Nonnegative(t.WindupSeconds, t.ActiveSeconds, t.RecoverySeconds, t.ChainWindowSeconds, t.CooldownSeconds))
                    errors.Add("timing out of range");
            }

            var g = catalog.SwordQi;
            if (g == null || !Positive(g.Length) || !Positive(g.Speed) || g.Stages < 1)
                errors.Add("sword qi geometry out of range");
            else if (!Positive(g.HalfWidth) || !Positive(g.SwordQiDamageCoefficient)
                || !Positive(g.TotalFlightSeconds)
                || Math.Abs(g.Length / g.Speed - g.TotalFlightSeconds) > 0.001f)
                errors.Add("sword qi flight mismatch");

            var p = catalog.Pvp;
            if (p == null || !Positive(p.PlayerMaxHp) || !Positive(p.WindVulnerabilityMultiplier)
                || !Positive(p.ControlImmunitySeconds) || !Positive(p.SameGroupVulnerabilityNoRefreshSeconds)
                || !Positive(p.ThunderDurationSeconds))
                errors.Add("pvp settings out of range");

            return errors;
        }

        private static bool Positive(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f;
        private static bool Nonnegative(params float[] values)
        {
            foreach (float v in values)
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return false;
            return true;
        }
        private static bool InRange(float v, float min, float max)
            => !float.IsNaN(v) && !float.IsInfinity(v) && v >= min && v <= max;
    }
}
```

- [x] **步骤 5：编写默认表**

`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/ElementalCatalogDefaults.cs`：

```csharp
using System.Collections.Generic;

namespace Cool.SyncCore
{
    public static class ElementalCatalogDefaults
    {
        public const int DefaultSwordTemplateId = 90001;
        public const int IceSwordTemplateId = 90002;
        public const int FireSwordTemplateId = 90003;
        public const int ThunderSwordTemplateId = 90004;
        public const int WindSwordTemplateId = 90005;

        public static ElementalCatalogDefinition Create() => new ElementalCatalogDefinition
        {
            DefaultWeaponTemplateId = DefaultSwordTemplateId,
            StartingWeaponTemplateIds = new List<int>
            {
                DefaultSwordTemplateId, IceSwordTemplateId, FireSwordTemplateId,
                ThunderSwordTemplateId, WindSwordTemplateId
            },
            Weapons = new List<WeaponDefinition>
            {
                new WeaponDefinition { TemplateId = DefaultSwordTemplateId, WeaponViewKey = "weapon/default", ElementId = 0 },
                new WeaponDefinition { TemplateId = IceSwordTemplateId, WeaponViewKey = "weapon/ice", ElementId = 1 },
                new WeaponDefinition { TemplateId = FireSwordTemplateId, WeaponViewKey = "weapon/fire", ElementId = 2 },
                new WeaponDefinition { TemplateId = ThunderSwordTemplateId, WeaponViewKey = "weapon/thunder", ElementId = 3 },
                new WeaponDefinition { TemplateId = WindSwordTemplateId, WeaponViewKey = "weapon/wind", ElementId = 4 },
            },
            Elements = new List<ElementDefinition>
            {
                new ElementDefinition { ElementId = 1, SwordQiViewKey = "swordqi/ice", StatusEffectIds = { 101 } },
                new ElementDefinition { ElementId = 2, SwordQiViewKey = "swordqi/fire", StatusEffectIds = { 201 } },
                new ElementDefinition { ElementId = 3, SwordQiViewKey = "swordqi/thunder", StatusEffectIds = { 301 } },
                new ElementDefinition { ElementId = 4, SwordQiViewKey = "swordqi/wind", StatusEffectIds = { 401 } },
            },
            Statuses = new List<StatusEffectDefinition>
            {
                new StatusEffectDefinition { EffectId = 101, EffectKind = 1, StackGroupId = 1, DurationSeconds = 2f, SlowRatio = 0.3f },
                new StatusEffectDefinition { EffectId = 201, EffectKind = 2, StackGroupId = 2, DurationSeconds = 3f, IntervalSeconds = 1f, DamagePerTick = 8f },
                new StatusEffectDefinition { EffectId = 301, EffectKind = 3, StackGroupId = 3, DurationSeconds = 0.8f },
                new StatusEffectDefinition { EffectId = 401, EffectKind = 4, StackGroupId = 4, DurationSeconds = 1.5f, IncomingDamageMultiplier = 2f },
            },
            AttackTimings = new List<AttackTimingDefinition>
            {
                new AttackTimingDefinition { SkillId = 10001, WindupSeconds = 0.30f, ActiveSeconds = 0.10f, RecoverySeconds = 0.20f, ChainWindowSeconds = 0.25f, CooldownSeconds = 0f },
                new AttackTimingDefinition { SkillId = 10002, WindupSeconds = 0.35f, ActiveSeconds = 0.10f, RecoverySeconds = 0.25f, ChainWindowSeconds = 0.30f, CooldownSeconds = 0.5f },
            },
            SwordQi = new ProjectileGeometryDefinition { Length = 12f, HalfWidth = 2f, Speed = 18f, Stages = 3, TotalFlightSeconds = 12f / 18f, SwordQiDamageCoefficient = 1f },
            Pvp = new PvpCombatSettings()
        };
    }
}
```

- [x] **步骤 6：编写 Unity EditMode 测试**

`Assets/Scripts/Tests/Editor/SyncCore/ElementalCatalogTests.cs`：

```csharp
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class ElementalCatalogTests
    {
        [Test]
        public void Default_Catalog_Has_Default_And_Elemental_Weapons()
        {
            var catalog = ElementalCatalogDefaults.Create();
            Assert.IsEmpty(ElementalCatalogValidator.Validate(catalog));
            Assert.IsNotNull(catalog.Weapons.Find(w => w.TemplateId == ElementalCatalogDefaults.DefaultSwordTemplateId));
            Assert.IsNotNull(catalog.Weapons.Find(w => w.TemplateId == ElementalCatalogDefaults.IceSwordTemplateId));
        }

        [Test]
        public void SwordQi_Length_Over_Speed_Matches_Flight_Time()
        {
            var g = ElementalCatalogDefaults.Create().SwordQi;
            Assert.AreEqual(g.Length / g.Speed, g.TotalFlightSeconds, 0.001f);
        }
    }
}
```

- [x] **步骤 7：接入共享配置产物并运行测试**

将 `Create()` 用于生成作者初值和测试夹具。配置源 `Server/Config/elemental_catalog.json` 随服务端复制到发布目录，同时作为 Unity Addressables 目录构建的输入。服务端启动时加载、校验并解析为同一个只读目录，再注入登录、装备、施法与发射入口；客户端先加载同一版配置再握手。校验默认剑存在且出现在初始清单，初始清单的模板全部可解析。沿用 `NpcCatalogHasher` 的稳定字段排序与规范化语义编码做法，指纹包含所有玩法字段，排除 `ContentHash` 自身。缺表、校验失败或入场指纹不匹配时停用该功能场景或拒绝进入。测试读取实际构建输入，并逐项核对 `CombatRules` 10002 的 12 米、2 米半宽、18 米/秒、3 段，以及 `SwordQi_Heavy.asset` 对应的射程、判定半径、速度和系数。服务端 `10002` 明确写入 `speed: 18f`，共享规则与资产对照覆盖默认值和非法参数。

运行：
- `bash tools/test-server.sh --filter "FullyQualifiedName~ElementalCatalogValidatorTests"`
- `bash tools/unity-tests.sh EditMode ElementalCatalogTests`

预期：全部 PASS。

- [x] **步骤 8：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental Assets/Scripts/Hotfix/GameSystems/SyncCore/CombatRules.cs Assets/Scripts/Tests/Editor/SyncCore/ElementalCatalogTests.cs Server/Config/elemental_catalog.json Server/Server3C.csproj Server/Server.Tests/Elemental
git commit -m "feat(synccore): add elemental catalog DTOs, validator and defaults"
```

---

## 任务 2：装备协议与施法协议字段

**文件：**
- 创建：`Assets/Scripts/AOT/Protocol/Messages/Equipment.cs`
- 修改：`Assets/Scripts/AOT/Protocol/MessageId.cs`
- 修改：`Assets/Scripts/AOT/Protocol/Messages/Combat.cs`
- 测试：`Server/Server.Tests/Protocol/EquipmentMessagesTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/Network/ProtocolRoundtripTests.cs`（追加用例）

- [x] **步骤 1：编写失败的协议往返测试**

```csharp
using Cool.Protocol;
using Xunit;

namespace KcpServer.Tests;

public class EquipmentMessagesTests
{
    [Fact]
    public void Equip_Request_Roundtrip_Preserves_Fields()
    {
        var decoded = (EquipWeaponRequest)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.EquipWeaponRequest,
            new EquipWeaponRequest { ClientEquipSeq = 7, WeaponInstanceId = 42, KnownEquipmentRevision = 3 })).message;

        Assert.Equal(7u, decoded.ClientEquipSeq);
        Assert.Equal(42L, decoded.WeaponInstanceId);
        Assert.Equal(3u, decoded.KnownEquipmentRevision);
    }

    [Fact]
    public void Cast_Broadcast_Roundtrip_Preserves_CastId_And_Speed()
    {
        var decoded = (CastBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CastBroadcast,
            new CastBroadcast
            {
                CastId = 99,
                CasterPlayerId = 1,
                CasterLifeId = 2,
                SkillId = 10002,
                AttackSpeedMultiplier = 1.25f,
                ElementId = 1,
                EquipmentRevision = 4
            })).message;

        Assert.Equal(99ul, decoded.CastId);
        Assert.Equal(2u, decoded.CasterLifeId);
        Assert.Equal(1.25f, decoded.AttackSpeedMultiplier);
        Assert.Equal(1, decoded.ElementId);
        Assert.Equal(4u, decoded.EquipmentRevision);
    }
}
```

- [x] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~EquipmentMessagesTests"`
预期：编译失败，`EquipWeaponRequest` / `MessageId.EquipWeaponRequest` 不存在。

- [x] **步骤 3：新增装备消息**

`Assets/Scripts/AOT/Protocol/Messages/Equipment.cs`：

```csharp
using ProtoBuf;

namespace Cool.Protocol
{
    [ProtoContract]
    public class EquipWeaponRequest : IMessage
    {
        [ProtoMember(1)] public uint ClientEquipSeq { get; set; }
        [ProtoMember(2)] public long WeaponInstanceId { get; set; }
        [ProtoMember(3)] public uint KnownEquipmentRevision { get; set; }
    }

    [ProtoContract]
    public class EquipWeaponConfirm : IMessage
    {
        [ProtoMember(1)] public uint ClientEquipSeq { get; set; }
        [ProtoMember(2)] public bool Accepted { get; set; }
        [ProtoMember(3)] public string RejectReason { get; set; } = "";
        [ProtoMember(4)] public uint EquipmentRevision { get; set; }
        [ProtoMember(5)] public long WeaponInstanceId { get; set; }
        [ProtoMember(6)] public int TemplateId { get; set; }
        [ProtoMember(7)] public int ElementId { get; set; }
    }
}
```

- [x] **步骤 4：追加消息 ID 并注册**

在 `Assets/Scripts/AOT/Protocol/MessageId.cs` 的战斗段末尾追加：

```csharp
        /// <summary>换装请求（客户端 → 服务器；属性剑气 v1）</summary>
        EquipWeaponRequest = 305,

        /// <summary>换装确认（服务器 → 客户端；含权威装备版本与武器摘要）</summary>
        EquipWeaponConfirm = 306,

        CastConfirm = 307,
        ProjectileLaunchBroadcast = 308,
        ProjectileStopBroadcast = 309,
```

在 `MessageCodec.cs` 的 `MessageTypeRegistry` 静态注册处登记这五个消息。使用真实 `EncodeToArray`/`Decode` 测试每个消息往返，包括停止先于发射、拒绝确认及装备列表的空列表与完整列表。注册表测试应验证每个新 ID 都能反查到唯一的消息类型。

在 `LoginRequest` 追加配置指纹，`LoginResponse` 追加对应指纹和装备用的 `WeaponInventoryBaseline`。基线包含 `LifeId`、`EquipmentRevision`、默认装备摘要和 `IList<WeaponInstanceSummary>`，每条实例摘要有 `InstanceId/TemplateId/ElementId`。协商新增 `ElementalSwordQiV1` 功能位，沿用 `SupportedFeatures/EnabledFeatures`。只对通过能力及指纹校验的会话发送新消息，开启此功能的战斗场景只接收匹配客户端。

`CastConfirm` 使用独立的 `[ProtoContract]`，含 `ClientCastSeq/Accepted/RejectReason/CastId/SkillId/CasterLifeId/EquipmentRevision/AttackSpeedMultiplier/AcceptedTick`。`ProjectileLaunchBroadcast` 含 `CastId/CasterPlayerId/CasterLifeId/CombatSceneId/LaunchTick/OriginX/Y/Z/Yaw/Speed/Range/ElementId`。`ProjectileStopBroadcast` 含 `CastId/CasterLifeId/CombatSceneId/StopTick/Reason/StopX/Z`。为各消息分配显式且唯一的 Protobuf tag，按设计文档的单位和缺省值编写往返测试。

- [x] **步骤 5：扩展 CastRequest / CastBroadcast / HitEntry**

在 `Assets/Scripts/AOT/Protocol/Messages/Combat.cs` 中，`CastRequest` 追加：

```csharp
        /// <summary>客户端施法序号（同一序号重试只返回原结论）</summary>
        [ProtoMember(8)] public uint ClientCastSeq { get; set; }

        /// <summary>客户端已确认的装备版本；不匹配时服务端拒绝并补发摘要</summary>
        [ProtoMember(9)] public uint EquipmentRevision { get; set; }
        [ProtoMember(10)] public uint CasterLifeId { get; set; }
```

`CastBroadcast` 追加：

```csharp
        [ProtoMember(15)] public ulong CastId { get; set; }
        [ProtoMember(16)] public uint CasterLifeId { get; set; }
        [ProtoMember(17)] public float AttackSpeedMultiplier { get; set; } = 1f;
        [ProtoMember(18)] public int ElementId { get; set; }
        [ProtoMember(19)] public uint EquipmentRevision { get; set; }
```

`HitEntry` 追加：

```csharp
        [ProtoMember(8)] public ulong CastId { get; set; }
        [ProtoMember(9)] public uint EventSeq { get; set; }
        [ProtoMember(10)] public ulong EventId { get; set; }
        [ProtoMember(11)] public byte DamageKind { get; set; }
        [ProtoMember(12)] public uint TargetLifeId { get; set; }
        [ProtoMember(13)] public uint CombatVersion { get; set; }
```

在 `Combat.cs` 末尾追加：

```csharp
    public enum DamageKind : byte { Direct = 0, Dot = 1 }
```

- [x] **步骤 6：运行测试验证通过**

运行：
- `bash tools/test-server.sh --filter "FullyQualifiedName~EquipmentMessagesTests"`
- `bash tools/unity-tests.sh EditMode ProtocolRoundtripTests`

预期：全部 PASS。

- [x] **步骤 7：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/Messages/Equipment.cs Assets/Scripts/AOT/Protocol/Messages/Combat.cs Assets/Scripts/AOT/Protocol/Messages/Login.cs Assets/Scripts/AOT/Protocol/Messages/NpcAction.cs Assets/Scripts/AOT/Protocol/MessageId.cs Assets/Scripts/AOT/Protocol/MessageCodec.cs Server/Server.Tests/Protocol/EquipmentMessagesTests.cs
git commit -m "feat(protocol): add equipment messages and cast identity/speed fields"
```

---

## 任务 3：服务端权威武器槽与换装处理

**文件：**
- 修改：`Server/Player/PlayerState.cs`
- 创建：`Server/Handler/EquipHandler.cs`
- 修改：`Server/Server3C.cs`（接线 handler）
- 修改：`Server/Handler/LoginHandler.cs`（正式登录发放、权威库存基线）
- 测试：`Server/Server.Tests/Handler/EquipHandlerTests.cs`

- [x] **步骤 1：编写失败的测试**

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class EquipHandlerTests
{
    private static EquipHandler CreateHandler()
        => new EquipHandler(ElementalCatalogDefaults.Create());

    [Fact]
    public void Rejects_Not_Owned()
    {
        var player = new PlayerState(1);
        player.WeaponInstanceTemplates[42] = ElementalCatalogDefaults.IceSwordTemplateId;
        var confirm = CreateHandler().ValidateAndApply(
            new EquipWeaponRequest { ClientEquipSeq = 1, WeaponInstanceId = 99, KnownEquipmentRevision = 0 }, player);
        Assert.False(confirm.Accepted);
        Assert.Equal("not-owned", confirm.RejectReason);
    }

    [Fact]
    public void Accepts_Owned_And_Bumps_Revision()
    {
        var player = new PlayerState(1);
        player.WeaponInstanceTemplates[42] = ElementalCatalogDefaults.IceSwordTemplateId;
        var confirm = CreateHandler().ValidateAndApply(
            new EquipWeaponRequest { ClientEquipSeq = 1, WeaponInstanceId = 42, KnownEquipmentRevision = 0 }, player);
        Assert.True(confirm.Accepted);
        Assert.Equal(1u, player.EquipmentRevision);
        Assert.Equal(42L, player.EquippedWeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.IceSwordTemplateId, player.EquippedWeaponTemplateId);
    }

    [Fact]
    public void Rejects_Stale_Revision()
    {
        var player = new PlayerState(1);
        player.WeaponInstanceTemplates[42] = ElementalCatalogDefaults.IceSwordTemplateId;
        player.EquipmentRevision = 3;
        var confirm = CreateHandler().ValidateAndApply(
            new EquipWeaponRequest { ClientEquipSeq = 1, WeaponInstanceId = 42, KnownEquipmentRevision = 2 }, player);
        Assert.False(confirm.Accepted);
        Assert.Equal("stale-revision", confirm.RejectReason);
    }
}
```

- [x] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~EquipHandlerTests"`
预期：编译失败，`EquipHandler` / `PlayerState.WeaponInstanceTemplates` 不存在。

- [x] **步骤 3：扩展 PlayerState**

在 `Server/Player/PlayerState.cs` 的字段区追加：

```csharp
        // 属性剑气 v1：权威武器槽与生命周期代次
        public long EquippedWeaponInstanceId { get; internal set; }
        public int EquippedWeaponTemplateId { get; internal set; }
        public uint EquipmentRevision { get; internal set; }
        public uint LifeId { get; internal set; } = 1;
        public readonly System.Collections.Generic.Dictionary<long, int> WeaponInstanceTemplates =
            new System.Collections.Generic.Dictionary<long, int>();
```

在 `TryRespawn` 成功分支追加：

```csharp
            LifeId++;
```

- [x] **步骤 4：编写 EquipHandler**

`Server/Handler/EquipHandler.cs`：

```csharp
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    public sealed class EquipHandler
    {
        private readonly ElementalCatalogDefinition _catalog;

        public EquipHandler(ElementalCatalogDefinition catalog)
        {
            _catalog = catalog;
        }

        public EquipWeaponConfirm ValidateAndApply(EquipWeaponRequest request, PlayerState player)
        {
            if (!player.WeaponInstanceTemplates.TryGetValue(request.WeaponInstanceId, out int templateId))
                return Reject(request, player, "not-owned");
            if (request.KnownEquipmentRevision != player.EquipmentRevision)
                return Reject(request, player, "stale-revision");

            var weapon = _catalog.Weapons.Find(w => w.TemplateId == templateId);
            if (weapon == null)
                return Reject(request, player, "unknown-template");

            player.EquippedWeaponInstanceId = request.WeaponInstanceId;
            player.EquippedWeaponTemplateId = templateId;
            player.EquipmentRevision++;
            return new EquipWeaponConfirm
            {
                ClientEquipSeq = request.ClientEquipSeq,
                Accepted = true,
                EquipmentRevision = player.EquipmentRevision,
                WeaponInstanceId = player.EquippedWeaponInstanceId,
                TemplateId = templateId,
                ElementId = weapon.ElementId
            };
        }

        private static EquipWeaponConfirm Reject(EquipWeaponRequest request, PlayerState player, string reason)
            => new EquipWeaponConfirm
            {
                ClientEquipSeq = request.ClientEquipSeq,
                Accepted = false,
                RejectReason = reason,
                EquipmentRevision = player.EquipmentRevision,
                WeaponInstanceId = player.EquippedWeaponInstanceId,
                TemplateId = player.EquippedWeaponTemplateId
            };
    }
}
```

- [x] **步骤 5：接线 Server3C**

在 `Server/Server3C.cs` 的 handler 字段区追加：

```csharp
        private EquipHandler? _equipHandler;
```

在服务器启动/初始化处构造（与 `_positionSyncHandler` 同处）：

```csharp
        _equipHandler = new EquipHandler(_elementalCatalog);
```

在消息路由处追加：

```csharp
                case MessageId.EquipWeaponRequest:
                {
                    var request = (EquipWeaponRequest)message;
                    long? playerId = GetPlayerId(session);
                    var player = playerId.HasValue ? _entityStore.GetPlayer(playerId.Value) : null;
                    if (player != null)
                    {
                        var confirm = _equipHandler!.ValidateAndApply(request, player);
                        await SendToSessionAsync(session, MessageId.EquipWeaponConfirm, confirm);
                    }
                    break;
                }
```

先由 `LoginHandler` 完成能力/指纹和场景校验，随后向 `EntityStore` 的权威 `PlayerState` 发放目录中的武器，分配唯一实例 ID，确定默认装备并生成登录基线。房间与战斗实体使用同一个服务端场景标识。会话内复活保留武器库存和装备版本，断线清理会话和重试缓存。

`ValidateAndApply` 的示例覆盖归属、模板和版本检查。上线接线同时实现：同一 `ClientEquipSeq` 在有界窗口回原确认且只改一次版本，窗口外的旧序号拒绝，登录和每个确认都返回可用于同步的权威装备摘要，拒绝也带当前元素。序号与装备校验同处服务端主循环，结果提交后发送。从正常登录入口取得实例再发换装包，测试同序号重试、过旧序号、拒绝时的完整当前摘要，以及重登和复活的生命周期差异。

- [x] **步骤 6：运行测试验证通过**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~EquipHandlerTests"`
预期：归属、版本、幂等、登录发放与生命周期测试全部通过。登录与换装测试走真实 Handler 和协议输入，无需手工填充玩家库存。

- [x] **步骤 7：Commit**

```bash
git add Server/Player/PlayerState.cs Server/Handler/EquipHandler.cs Server/Handler/LoginHandler.cs Server/Server3C.cs Server/Server.Tests/Handler/EquipHandlerTests.cs
git commit -m "feat(server): add authoritative weapon slot and equip handler"
```

---

## 任务 4：服务端 CastId 分配与 WeaponCastSnapshot

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/WeaponCastSnapshot.cs`
- 修改：`Server/Handler/CastHandler.cs`
- 测试：`Server/Server.Tests/Handler/CastSnapshotTests.cs`

- [x] **步骤 1：编写失败的测试**

```csharp
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CastSnapshotTests
{
    [Fact]
    public void Decide_Allocates_Monotonic_CastId()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "P1");
        player.WeaponInstanceTemplates[10] = ElementalCatalogDefaults.IceSwordTemplateId;
        var resolver = new CombatResolver(store);
        var a = resolver.Decide(1, new CastRequest { SkillId = 10001 }, 100);
        var b = resolver.Decide(1, new CastRequest { SkillId = 10001 }, 200);
        Assert.True(a.Accepted);
        Assert.True(b.Accepted);
        Assert.True(b.Broadcast!.CastId > a.Broadcast!.CastId);
    }

    [Fact]
    public void Broadcast_Carries_Weapon_Snapshot()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "P1");
        player.WeaponInstanceTemplates[10] = ElementalCatalogDefaults.IceSwordTemplateId;
        player.EquippedWeaponInstanceId = 10;
        player.EquippedWeaponTemplateId = ElementalCatalogDefaults.IceSwordTemplateId;
        player.EquipmentRevision = 2;
        var resolver = new CombatResolver(store);
        var result = resolver.Decide(1, new CastRequest
        {
            SkillId = 10002, ClientCastSeq = 1, EquipmentRevision = 2, CasterLifeId = player.LifeId
        }, 100);
        Assert.True(result.Accepted);
        Assert.Equal(2u, result.Broadcast!.EquipmentRevision);
        Assert.Equal(1, result.Broadcast.ElementId);
        Assert.Equal(1f, result.Broadcast.AttackSpeedMultiplier);
    }
}
```

- [x] **步骤 2：运行测试验证失败**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~CastSnapshotTests"`
预期：编译失败或断言失败（`CastId` 恒 0、`ElementId` 恒 0）。

- [x] **步骤 3：新增 WeaponCastSnapshot**

`Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/WeaponCastSnapshot.cs`：

```csharp
namespace Cool.SyncCore
{
    public readonly struct WeaponCastSnapshot
    {
        public readonly long WeaponInstanceId;
        public readonly int TemplateId;
        public readonly int ElementId;
        public readonly uint EquipmentRevision;
        public readonly float BaseAttack;
        public readonly float AttackSpeedMultiplier;
        public readonly float SwordQiDamageMultiplier;

        public WeaponCastSnapshot(long weaponInstanceId, int templateId, int elementId,
            uint equipmentRevision, float baseAttack, float attackSpeedMultiplier, float swordQiDamageMultiplier)
        {
            WeaponInstanceId = weaponInstanceId;
            TemplateId = templateId;
            ElementId = elementId;
            EquipmentRevision = equipmentRevision;
            BaseAttack = baseAttack;
            AttackSpeedMultiplier = attackSpeedMultiplier;
            SwordQiDamageMultiplier = swordQiDamageMultiplier;
        }
    }
}
```

- [x] **步骤 4：修改 CastHandler**

在 `Server/Handler/CastHandler.cs` 的 `CombatResolver` 类中：

1. 追加字段：

```csharp
        // 从 Server3C 注入启动时通过校验的共享目录，测试注入夹具。
        private readonly ElementalCatalogDefinition _elementalCatalog;
        private ulong _nextCastId;
```

2. 在 `PendingCast` 追加：

```csharp
            public ulong CastId;
            public WeaponCastSnapshot Weapon;
```

3. 在 `Decide` 中、生成 `resolution.Broadcast` 之前解析武器快照：

```csharp
            var weapon = ResolveWeapon(caster);
            ulong castId = ++_nextCastId;
```

4. 在 `new CastBroadcast { ... }` 中追加：

```csharp
                CastId = castId,
                CasterLifeId = caster.LifeId,
                AttackSpeedMultiplier = weapon.AttackSpeedMultiplier,
                ElementId = weapon.ElementId,
                EquipmentRevision = weapon.EquipmentRevision
```

5. 在 `_pending.Add(new PendingCast(...) { Runner = runner, InstanceId = ... })` 中追加：

```csharp
                CastId = castId,
                Weapon = weapon
```

6. 在类内新增：

```csharp
        private WeaponCastSnapshot ResolveWeapon(PlayerState caster)
        {
            var definition = _elementalCatalog.Weapons.Find(w => w.TemplateId == caster.EquippedWeaponTemplateId)
                ?? _elementalCatalog.Weapons.Find(w => w.TemplateId == _elementalCatalog.DefaultWeaponTemplateId)!;
            return new WeaponCastSnapshot(caster.EquippedWeaponInstanceId, definition.TemplateId,
                definition.ElementId, caster.EquipmentRevision, definition.BaseAttack,
                definition.AttackSpeedMultiplier, definition.SwordQiDamageMultiplier);
        }
```

完成以下运行时接线后再验收这一步。

- `CombatResolver` 使用唯一的世界级分配器，所有玩家及 NPC 共享 ID。NPC 自有 `NpcActionSnapshot.InstanceId` 另行保存。每个由当前施法产生的 `HitEntry` 在 `ApplyHit` 写入 `pending.CastId` 与目标生命代次；场景过滤应用到挥砍和剑气候选以及广播收件人。
- `Decide` 在安排判定和扣资源前验证当前生命、装备版本与 `ClientCastSeq`。本人始终收到 `CastConfirm`；重复请求重发原结论，不重播观察者广播、不安排新判定。装备版本过旧时同时给本人当前装备摘要。换装和施法按主循环处理，客户端换装待确认时暂缓发起新普攻。
- 新增共享普攻参数解析，将 `WeaponCastSnapshot`、默认武器攻击力与技能基线合成伤害及时序。`ApplyHit` 的直接普攻基础伤害乘武器攻击力比值，剑气分支另乘技能系数和武器剑气倍率。区分挥砍与剑气时使用命中段类型，避免给 `KeepsSwing` 的挥砍乘剑气倍率。主动技能、NPC 保持原默认伤害。
- 用共享时序和施法快照换算首判、接续与冷却 tick。按施法时装备固定下一次允许出手的 tick，换装不能提前解锁。`10001` 零基础冷却仍受实际接续窗约束。使用 tick 计算时保留首判和冷却原基线的对照测试。
- 服务端测试至少使用两把不同攻击力和攻速的剑，验证真实 `DrainResolved` 输出的首判 tick、轻击/重击起手、剑气伤害、`HitEntry.CastId` 和冷却。补同序号重试、过期装备或生命、先施法后换装、两名 NPC 具有相同动作实例 ID。竞态要走正常请求输入，并固定随机源复现结果。

- [x] **步骤 5：运行测试验证通过**

运行：`bash tools/test-server.sh --filter "FullyQualifiedName~CastSnapshotTests"`
预期：广播、伤害、时序、输入幂等和生命周期测试全部通过。再跑 `bash tools/test-server.sh --filter "FullyQualifiedName~CombatResolver"` 确认既有测试不回归。

- [x] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore/Elemental/WeaponCastSnapshot.cs Server/Handler/CastHandler.cs Server/Server.Tests/Handler/CastSnapshotTests.cs
git commit -m "feat(server): allocate cast ids and freeze weapon cast snapshots"
```

---

## 任务 5：客户端装备确认应用与武器表现切换

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientEquipmentState.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/CombatNetworkBridge.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`、`ClientNetworkManager.cs`、`SkillCastReporter.cs`（登录基线、请求、权威确认）
- 修改：`Assets/Scripts/Hotfix/GameSystems/Bag/` 的背包面板、物品模板与装备交互
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/ClientEquipmentStateTests.cs`

- [x] **步骤 1：编写失败的测试**

```csharp
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class ClientEquipmentStateTests
    {
        [Test]
        public void Applies_Newer_Revision_And_Ignores_Stale()
        {
            var state = new ClientEquipmentState();
            Assert.IsTrue(state.TryApply(new EquipWeaponConfirm
            {
                Accepted = true, EquipmentRevision = 2, WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
            }));
            Assert.AreEqual(2u, state.Revision);
            Assert.AreEqual(1, state.ElementId);

            Assert.IsFalse(state.TryApply(new EquipWeaponConfirm
            {
                Accepted = true, EquipmentRevision = 1, WeaponInstanceId = 43, TemplateId = 90003, ElementId = 2
            }));
            Assert.AreEqual(42L, state.WeaponInstanceId);
        }
    }
}
```

- [x] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode ClientEquipmentStateTests`
预期：编译失败，`ClientEquipmentState` 不存在。

- [x] **步骤 3：编写 ClientEquipmentState**

`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientEquipmentState.cs`：

```csharp
using System;
using Cool.Protocol;

namespace Hotfix.GameSystems.SyncLayer
{
    public sealed class ClientEquipmentState
    {
        public uint Revision { get; private set; }
        public long WeaponInstanceId { get; private set; }
        public int TemplateId { get; private set; }
        public int ElementId { get; private set; }

        public event Action<EquipWeaponConfirm>? Changed;

        public bool TryApply(EquipWeaponConfirm confirm)
        {
            if (confirm == null || confirm.EquipmentRevision <= Revision) return false;
            Revision = confirm.EquipmentRevision;
            WeaponInstanceId = confirm.WeaponInstanceId;
            TemplateId = confirm.TemplateId;
            ElementId = confirm.ElementId;
            Changed?.Invoke(confirm);
            return true;
        }
    }
}
```

- [x] **步骤 4：接线 CombatNetworkBridge**

在 `CombatNetworkBridge` 给 `EquipWeaponConfirm`、`CastConfirm` 注册消息订阅。`OnMessage` 只入队，`Update` 主线程出队时执行：

```csharp
                case MessageId.EquipWeaponConfirm:
                    ApplyEquipConfirm((EquipWeaponConfirm)message);
                    break;
```

在类中追加字段与事件：

```csharp
        private readonly ClientEquipmentState _equipment = new ClientEquipmentState();
        public ClientEquipmentState Equipment => _equipment;
        public event Action<EquipWeaponConfirm>? OnEquipmentChanged;
```

在构造函数末尾订阅：

```csharp
        _equipment.Changed += confirm => OnEquipmentChanged?.Invoke(confirm);
```

宿主（当前初始化 `CombatNetworkBridge` 的地方，或 `Sys3CEntry`）接线：

```csharp
        _combatBridge.OnEquipmentChanged += confirm =>
            _attackHandler.EquipWeapon(confirm.TemplateId, confirm.ElementId);
```

`CharacterAttackHandler` 增加武器工厂与按表切换：

```csharp
        private Func<int, int, IWeapon>? _weaponFactory;

        public void SetWeaponFactory(Func<int, int, IWeapon> factory) => _weaponFactory = factory;

        public void EquipWeapon(int templateId, int elementId)
        {
            var weapon = _weaponFactory?.Invoke(templateId, elementId);
            if (weapon != null) EquipWeapon(weapon);
        }
```

用 `ClientNetworkHost` 将登录基线应用到本人装备状态和背包。`ClientEquipmentState` 增加独立的登录初始化与断线清理入口，使初始版本 0 可作为基线应用。同一会话的普通确认只接受更新版本，拒绝确认的结果用于关闭待确认态，有较新权威摘要时用它恢复当前武器。完成对应背包行的装备操作，通过现有联机发送入口传 `ClientEquipSeq/WeaponInstanceId/KnownEquipmentRevision`。施法上报增加 `ClientCastSeq/EquipmentRevision/CasterLifeId`，本人消费 `CastConfirm` 处理预测、重复及拒绝。

原有 `EquipWeapon(IWeapon weapon)` 保持原签名。实际实现时把目录资源键接到手持模型挂点与 Addressables 加载，加载结束核对会话和装备版本后再替换模型。过期加载释放引用。工作流测试从登录基线渲染背包并点装备，注入成功、重复、过期和带新摘要的拒绝确认，在 `Update` 后断言背包、当前模板及可视模型只更新一次。

- [x] **步骤 5：运行测试验证通过**

运行：`bash tools/unity-tests.sh EditMode ClientEquipmentStateTests`
预期：PASS。

- [x] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer Assets/Scripts/Hotfix/GameSystems/Bag Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs Assets/Scripts/Tests/Editor/SyncLayer/ClientEquipmentStateTests.cs
git commit -m "feat(sync): apply authoritative equipment confirmations"
```

---

## 任务 6：攻击 Animator `AttackSpeed` 参数

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimHashes.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/AttackSpeedParameterTests.cs`
- 资产：攻击 Animator state 增加 float 参数 `AttackSpeed`（在 Unity Editor 中完成，或由 MCP/脚本批量加；本任务先加代码侧写入，资产改动在步骤 6 验证）

- [x] **步骤 1：编写失败的测试**

```csharp
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class AttackSpeedParameterTests
    {
        [Test]
        public void Clamps_And_Stores_Attack_Speed()
        {
            var go = new GameObject("animator-test");
            var animator = go.AddComponent<RemoteEntityAnimator>();
            try
            {
                animator.PlayAttack("Attack", 1f, 1.25f);
                Assert.AreEqual(1.25f, animator.AttackSpeedForTest, 0.001f);

                animator.PlayAttack("Attack", 1f, 0.01f);
                Assert.AreEqual(0.25f, animator.AttackSpeedForTest, 0.001f);

                animator.PlayAttack("Attack", 1f, 99f);
                Assert.AreEqual(4f, animator.AttackSpeedForTest, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
```

- [x] **步骤 2：运行测试验证失败**

运行：`bash tools/unity-tests.sh EditMode AttackSpeedParameterTests`
预期：编译失败，`PlayAttack` 没有 3 参数重载、`AttackSpeedForTest` 不存在。

- [x] **步骤 3：新增 AnimHashes 与 Animator 写入**

在 `AnimHashes.cs` 追加：

```csharp
        public static readonly int AttackSpeed = Animator.StringToHash("AttackSpeed");
```

在 `RemoteEntityAnimator.cs` 中，把原 `PlayAttack(string, float)` 的主体移到 3 参数重载，2 参数重载只做转发：

```csharp
        private float _attackSpeed = 1f;
        internal float AttackSpeedForTest => _attackSpeed;

        internal void PlayAttack(string trigger, float duration)
            => PlayAttack(trigger, duration, 1f);

        internal void PlayAttack(string trigger, float duration, float attackSpeed)
        {
            if (_dead || _deathPhase == DeathPhase.Down) return;
            if (_deathPhase == DeathPhase.GettingUp) _deathPhase = DeathPhase.None;

            _attackSpeed = Mathf.Clamp(attackSpeed, 0.25f, 4f);
            _attackRemaining = duration / _attackSpeed;
            _attackTrigger = trigger;
            _attackTriggerApplied = false;
            RefreshLayers();
        }
```

远端把倍率与攻击触发一起保存，在现有 RefreshLayers 真正应用攻击 trigger 时同步写入当前 Animator。播放完成、取消或模型重绑时恢复默认值或重放当前快照。怪物技能沿用现有时序，NPC 动作同步归 Plan 02。

- [x] **步骤 4：让远端施法携带攻速**

在 `RemoteCastPresenter.OnCastBroadcast` 中，把：

```csharp
            view.Animator?.PlayAttack(data.AnimatorTrigger,
                (data is ChargedSkillData ? data.ReleaseClip : data.CastClip)?.length ?? DefaultAttackDuration);
```

改为：

```csharp
            view.Animator?.PlayAttack(data.AnimatorTrigger,
                (data is ChargedSkillData ? data.ReleaseClip : data.CastClip)?.length ?? DefaultAttackDuration,
                broadcast.AttackSpeedMultiplier);
```

- [x] **步骤 5：本地施法写入同一参数**

在 `CharacterAttackHandler` 增加序列化 Animator 与写入方法（避免 `GetComponent`）：

```csharp
        [SerializeField] private Animator _animator;

        public void SetAttackSpeed(float multiplier)
        {
            if (_animator != null)
                _animator.SetFloat(AnimHashes.AttackSpeed, Mathf.Clamp(multiplier, 0.25f, 4f));
        }
```

本地 `SkillCoordinator` 在普攻开始时冻结已确认的装备，按任务 4 的共享参数派生技能执行时序。在设置攻击 trigger 前写攻速参数，以同一倍率驱动 `SkillExecutor` 的执行、判定及接续计时，冷却用已缩放的秒数。每段时序只缩放一次。本人用 `CastConfirm` 对账，拒绝时停止对应预测。下一次普攻才读取新装备。

- [x] **步骤 6：在 Animator 控制器加参数并验证**

在 `Assets/RpgDuo/Animator/Character3C.controller` 的轻击、重击 state 上绑定 `AttackSpeed` 浮点乘数（默认 1）。验证本地与远端实际控制器。EditMode 检查真实资产参数与 state 绑定，PlayMode 断言 0.25、1、4 倍时动画窗口、连段与共享判定时序一致。

`bash tools/unity-tests.sh EditMode AttackSpeedParameterTests`
预期：PASS；再用 Unity Editor 播放 10001/10002 确认动画不被截断。

- [x] **步骤 7：Commit**

```bash
git add Assets/RpgDuo/Animator/Character3C.controller Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimHashes.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityAnimator.cs Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillCoordinator.cs Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs Assets/Scripts/Tests/Editor/SyncLayer/AttackSpeedParameterTests.cs
git commit -m "feat(animation): drive attack speed through animator parameter"
```

---

## 任务 7：权威发射、提前终止和两端按 CastId 跟踪剑气

**文件**

- 服务端 `Server/Handler/CastHandler.cs`、`Server/Server3C.cs`，以及任务 2 已注册的发射和终止消息。
- 客户端 `CombatNetworkBridge.cs`、`ClientNetworkHost.cs`、`RemoteCastPresenter.cs`、`SkillExecutor.cs`、`SwordQiSpawner.cs`。
- 测试 `Server/Server.Tests/Handler/CombatResolverSwordQiLaunchTests.cs`、`Assets/Scripts/Tests/Editor/SyncLayer/RemoteCastPresenterCastIdTests.cs`。

- [x] **步骤 1：编写能真实走判定的失败测试**

服务端由两个合法重击请求产生施法 A/B，令 B 被格挡，断言发射、命中及提前终止的 `CastId` 均指向 B，后方实体免于该道剑气的命中。将服务器生成的消息编码并解码作为同一组协议测试的输入。客户端测试通过桥接消费发射、伤害及终止消息，分别绑定本地及远端呈现器，断言各自只生成 A/B 各一次、只停止 B。覆盖终止先到、重复发射、重复终止、延迟发射和派生自旧生命周期的事件。不通过直接手填 `HitEntry` 来替代服务端关联的测试。

- [x] **步骤 2：接线服务端发射和结束**

重击在权威发射 tick 完成近身挥砍和发射。对存活并仍属原场景的施法者，冻结一份发射描述，服务端的线上判定和客户端发射由同一 XZ 起点/方向驱动。速度、射程、半宽和段数取已验证的共享 `10002`。绕开死亡或离场后的未发射施法。发射消息独立于命中结果，空挥也发射。Plan 01 默认采用设计文档的段末调度，使用默认三段做首段实测；若为满足同一文档的验收门槛必须细分，则保留 12 米、18 米/秒和 2 米半宽并记录实测决策。第一段也从同一发射时钟计算。

格挡、死亡、离场提前终止由服务端生成 `ProjectileStopBroadcast`，带原 `CastId`、停点、原因和服务器 tick。发射后生成的命中结果用同一个 ID。首先发送给施法者和同场景有视野的客户端；订阅了该发射的会话即使随后移出距离也要收到其终止。观察者中途进入视野补发在途基线由 Plan 02 接入。

- [x] **步骤 3：本地和远端统一消费服务端剑气**

`CastBroadcast` 只驱动远端攻击动作。启用该功能的联机客户端在 `SkillExecutor.OnSwordQiFrame` 不再创建剑气，离线照常。`CombatNetworkBridge` 把发射和终止封送到主线程，由 `RemoteCastPresenter` 或共用发射呈现器为本人及远端创建剑气。以 `CastId` 检查重复，并按生命周期、场景和飞行窗口淘汰缓存。晚到的发射按服务端 tick 补进度，终止先到时暂存对应停点或终态。视觉只有权威停点能提前终止；同一流程移除 `StopNearest` 和按 `casterId + skillId` 猜剑气的旧联机分支。现有火球的表现路径保持原接口。

- [x] **步骤 4：验证默认剑和两端链路**

运行 `bash tools/test-server.sh --filter "FullyQualifiedName~CombatResolverSwordQiLaunchTests|FullyQualifiedName~CombatResolverSwordQiBlockTests"` 和 `bash tools/unity-tests.sh EditMode RemoteCastPresenterCastIdTests`。先核对 12 米默认剑、首段和末段，再验证冰剑发射后换火剑，两个客户端收到同一 `CastId`、起点、元素和停点。两端只停 B。

- [x] **步骤 5：提交本任务涉及的服务端、协议、客户端和测试文件**

---


## 任务 8：阶段验证与 README 更新

**文件：**
- 修改：`Assets/Scripts/AOT/Protocol/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Skills/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Bag/README.md`、`Server/README.md`

- [x] **步骤 1：跑全部相关测试**

```bash
bash tools/test-server.sh --filter "FullyQualifiedName~ElementalCatalogValidatorTests|FullyQualifiedName~EquipmentMessagesTests|FullyQualifiedName~EquipHandlerTests|FullyQualifiedName~CastSnapshotTests|FullyQualifiedName~CombatResolver|FullyQualifiedName~LoginRosterTests"
bash tools/unity-tests.sh EditMode ElementalCatalogTests
bash tools/unity-tests.sh EditMode ClientEquipmentStateTests
bash tools/unity-tests.sh EditMode AttackSpeedParameterTests
bash tools/unity-tests.sh EditMode RemoteCastPresenterCastIdTests
bash tools/unity-tests.sh EditMode CombatNetworkBridgeTests
```

预期：全部 PASS；若 testhost 抛 `Win32Exception(5)`，服务端命令改用 `tools/test-server-inprocess.sh`。

- [x] **步骤 2：更新模块 README**

在每个 README 中写清：
- 协议：`EquipWeaponRequest/Confirm` 的 ID 与字段；`CastBroadcast.CastId/AttackSpeedMultiplier/ElementId` 的语义。
- SyncCore：`ElementalCatalogDefinition` 是共享真值；构建校验命令与失败处理。
- SyncLayer：`ClientEquipmentState` 按版本应用；`RemoteCastPresenter` 按 `CastId` 跟踪。
- Skills/Sys3C：攻击 state 的 `AttackSpeed` 参数；本地与远端同源。
- Bag/Server：登录发放与装备确认的生命周期、场景隔离、权威普攻和投射物输出。

- [x] **步骤 3：手动双客户端冒烟**

1. 启动服务端：`dotnet Server/bin/Debug/net8.0/Server3C.dll`（用 `dotnet <dll>`，不要 `dotnet run`：apphost 会让 `HttpListener.Start` 抛“句柄无效”）。
2. 启动两个客户端，登录同一场景。
3. 通过背包选择冰剑，在途换火剑后再施放。核对两端各次发射的 `CastId`、元素、轨迹和格挡停点。两发 ID 不同，且原冰剑气保持冰属性。
4. 从测试配置选择另一把攻击力、攻速不同的剑，核对两端动画节奏、服务端首判 tick、扣血和最早出手时间。旧换装确认、重发施法请求、消息乱序分别注入验证。用不同房间、相同坐标的玩家对照，断言不互相命中也不收到对方剑气消息。

- [x] **步骤 4：Commit**

```bash
git add Assets/Scripts/AOT/Protocol/README.md Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md Assets/Scripts/Hotfix/GameSystems/Skills/README.md Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md Assets/Scripts/Hotfix/GameSystems/Bag/README.md Server/README.md
git commit -m "docs: record elemental foundation protocol and sync behavior"
```

---

## 完成定义

- 任务 1–8 全部勾选，测试命令通过。
- 双客户端从真实登录起通过背包换装，实际伤害与攻速、元素快照、发射和停点在同一 `CastId` 闭合；两端统一用同一份配置产物。跨场景与重试用例通过。
- 未启用项（状态结算、取消、投影、PvP 预算）在 README 中明确标注为 02–05 计划，不写成已打通。
- 计划 02 可以直接引用本计划的 `ElementalCatalogDefinition`、`WeaponCastSnapshot`、`CastBroadcast.CastId` 与 `ClientEquipmentState`。

## 执行说明（2026-11-14）

- 任务 8 的双客户端冒烟以 PlayMode `ElementalTwoClientTests` 自动化执行：两个真实
  `ClientNetworkManager` 连接本机 `Server3C`（127.0.0.1:8123），完成真实登录、权威换装、
  施法与同一 `CastId` 发射校验，结果 XML 见 `TestResults/elemental-two-client.xml`。
- Unity 测试使用 batchmode（不能带 `-quit`，Test Framework 自行退出）：
  `Unity.exe -batchmode -nographics -projectPath <worktree> -runTests
  -testPlatform EditMode|PlayMode -testFilter <Class> -testResults <xml> -logFile <log>`。
- 未做目测项：在编辑器里逐帧播放 10001/10002 确认“动画不被截断”。等价断言已覆盖
  `AttackSpeedParameterTests`（0.25/1/4 钳制、状态机时间缩放、真实控制器 AttackSpeed 绑定），
  但缺少实际动画播放的人眼/录屏证据。
