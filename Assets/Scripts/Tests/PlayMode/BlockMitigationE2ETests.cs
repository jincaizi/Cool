using System.Collections;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using KcpNet;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 格挡减伤（D-P4-8 服务端化）的**联网端到端验收**：同一会话内先不防御挨一刀（对照组 = 全额），
    /// 再开着防御挨一刀（防御标志经 MovementReporter 上行 ⇒ 服务端判格挡 ⇒ 伤害 × BlockDamageScale
    /// + 格挡标志随 DamageResult 回报双方）。
    ///
    /// 装置照 CombatSyncTests（双 ClientNetworkManager + KcpNetClientAdapter），但**每帧驱动**
    /// （Manager.Update(Time.deltaTime)），位置/朝向/防御标志全部经真实上报链上行。
    /// 需要**已运行的服务器**（localhost:8000，且是含格挡逻辑的新构建）。
    /// 期望值不写死：从共享表 CombatRules 读（BaseDamage/CritMultiplier/BlockDamageScale）。
    /// </summary>
    public class BlockMitigationE2ETests
    {
        private const int DuelSkillId = 10001; // BA_Combo1：dmg 50 crit 15%，无冷却——两次挥砍不必等冷却

        private sealed class Ctx
        {
            public readonly KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public readonly List<DamageResult> Damages = new List<DamageResult>();
        }

        private sealed class Rig : System.IDisposable
        {
            public readonly GameObject Root;
            public readonly Ctx A = new Ctx();
            public readonly Ctx B = new Ctx();
            /// <summary>B 的防御状态（上报真值源；测试直接翻它——生产里接 CharacterController.IsDefending）</summary>
            public bool Defending;

            public Rig()
            {
                Root = new GameObject("BlockMitigationRoot");
                A.Manager = new ClientNetworkManager(A.Adapter, new RemoteEntityRegistry(Root.transform), "Block_A");
                B.Manager = new ClientNetworkManager(B.Adapter, new RemoteEntityRegistry(Root.transform), "Block_B");
                // A：原点朝 +Z（面朝 B，施法锥 ±60° 需要）；不动不转
                A.Manager.AttachMovementReporter(() => new Vec3(0f, 0f, 0f), () => 0f, () => 0f);
                // B：+Z 1.5m 处朝 -Z（面朝 A，格挡锥 ±60° 需要）；防御标志随每次上报上行
                B.Manager.AttachMovementReporter(() => new Vec3(0f, 0f, 1.5f), () => 180f, () => 0f,
                    defendStateProvider: () => Defending);
                Capture(A);
                Capture(B);
            }

            private static void Capture(Ctx ctx)
                => ctx.Adapter.MessageReceived += (id, msg) =>
                {
                    if (id == MessageId.DamageResult) ctx.Damages.Add((DamageResult)msg);
                };

            public void Dispose()
            {
                A.Manager.Dispose();
                B.Manager.Dispose();
                Object.Destroy(Root);
            }
        }

        [UnityTest]
        public IEnumerator Blocked_Hit_Is_Mitigated_And_Full_Hit_Is_Not()
        {
            var rig = new Rig();
            try
            {
                yield return ConnectAndWaitInWorld(rig.A, rig.B);
                Assert.IsTrue(CombatRules.TryGet(DuelSkillId, out var entry), "技能不在共享表内——期望值无从计算");
                long casterId = rig.A.Manager.LocalPlayerId;

                // 让首次上报落进服务端（位置 + 朝向 + 未防御），再做对照挥砍
                yield return DriveFor(rig, 0.5f);

                // ① 对照组：B 未防御 ⇒ 全额（暴击域 {50, 75}）
                yield return CastAndAwaitCast(rig.A, rig.B, entry!, expectedCastIndex: 0);
                var plain = FindCast(rig.B.Damages, casterId, 0);
                Assert.IsNotNull(plain, "B 没收到对照组伤害结果");
                Assert.AreEqual(1, plain!.Hits.Count, "Single 技能应恰好命中 1 个目标");
                var plainHit = plain.Hits[0];
                Assert.IsFalse(plainHit.IsBlocked, "未防御却被判格挡——格挡判定轴接错");
                AssertDamageInDomain(plainHit.Damage, entry!, blocked: false, "对照组（未防御）");

                // ② 防御组：B 上报防御中（面朝 A）⇒ 格挡 ⇒ 伤害 × BlockDamageScale
                rig.Defending = true;
                yield return DriveFor(rig, 0.4f); // 覆盖至少数个上报周期——服务端必须已看到 IsDefending
                yield return CastAndAwaitCast(rig.A, rig.B, entry!, expectedCastIndex: 1);
                var blockedCast = FindCast(rig.B.Damages, casterId, 1);
                Assert.IsNotNull(blockedCast, "B 没收到防御组伤害结果");
                Assert.AreEqual(1, blockedCast!.Hits.Count, "Single 技能应恰好命中 1 个目标");
                var blockedHit = blockedCast.Hits[0];
                Assert.IsTrue(blockedHit.IsBlocked, "防御中 + 面朝攻击者却未判格挡（上行标志丢失？几何反了？）");
                AssertDamageInDomain(blockedHit.Damage, entry!, blocked: true, "防御组");

                // ③ 服务端权威余血口径（T12 甲-2）：第二击余血 = 第一击余血 − 本击伤害
                Assert.AreEqual(plainHit.TargetRemainingHp - blockedHit.Damage, blockedHit.TargetRemainingHp, 0.01f,
                    "格挡分支的余血不是扣后权威值");

                // ④ 双端一致：caster 收到的同一条命中数值/标志必须与受击者相同。
                // A 的副本是**独立的一次下行发送**（BroadcastDamageAsync 逐收件人发）——
                // 必须等它到达，不能假设"与 B 的副本同帧必到"（实测会差几帧）
                yield return AwaitCasterCopy(rig, casterId, expectedCastIndex: 1);
                var atCaster = FindCast(rig.A.Damages, casterId, 1);
                Assert.IsNotNull(atCaster, "A（caster）没收到第 2 条命中反馈");
                Assert.AreEqual(1, atCaster!.Hits.Count, "Single 技能应恰好命中 1 个目标");
                var atCasterHit = atCaster.Hits[0];
                Assert.AreEqual(blockedHit.Damage, atCasterHit.Damage, 0.001f, "双端伤害数字不一致");
                Assert.IsTrue(atCasterHit.IsBlocked, "格挡标志未随下发包到 caster");

                Debug.Log($"[BlockMitigationE2ETests] 未防御 {plainHit.Damage:F1}（余血 {plainHit.TargetRemainingHp:F1}）"
                    + $" → 防御中 {blockedHit.Damage:F1}（余血 {blockedHit.TargetRemainingHp:F1}，格挡={blockedHit.IsBlocked}）");
            }
            finally
            {
                rig.Dispose();
            }
        }

        // ---------------------------------------------------------------- 助手

        private static IEnumerator ConnectAndWaitInWorld(Ctx a, Ctx b)
        {
            a.Manager.ConnectAndLogin("127.0.0.1", 8000);
            yield return WaitInWorld(a.Manager, "A");
            b.Manager.ConnectAndLogin("127.0.0.1", 8000);
            yield return WaitInWorld(b.Manager, "B");
        }

        private static IEnumerator WaitInWorld(ClientNetworkManager manager, string who)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (manager.CurrentState != ClientNetworkManager.State.InWorld
                   && Time.realtimeSinceStartup < deadline)
            {
                manager.Update(Time.deltaTime);
                yield return null;
            }
            Assert.AreEqual(ClientNetworkManager.State.InWorld, manager.CurrentState,
                $"{who} 未进世界（服务器没起？端口 8000 被占？协议版本不匹配？）");
        }

        /// <summary>连续驱动双端若干秒（每帧 Update——上报节流/响应处理都靠它）。</summary>
        private static IEnumerator DriveFor(Rig rig, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                DriveOneFrame(rig);
                yield return null;
            }
        }

        /// <summary>A 发起一次挥砍（显式 TargetPlayerId=B——避开自动目标的 ghost 干扰）并等到 B
        /// 收到 A 的第 <paramref name="expectedCastIndex"/>+1 条伤害结果。</summary>
        private static IEnumerator CastAndAwaitCast(Ctx a, Ctx b, SkillEntry entry, int expectedCastIndex)
        {
            a.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = DuelSkillId,
                TargetPlayerId = b.Manager.LocalPlayerId,
                Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
            });

            float deadline = Time.realtimeSinceStartup + 10f;
            while (CountCasts(b.Damages, a.Manager.LocalPlayerId) <= expectedCastIndex
                   && Time.realtimeSinceStartup < deadline)
            {
                DriveOneFrame(a, b);
                yield return null;
            }
            Assert.Greater(CountCasts(b.Damages, a.Manager.LocalPlayerId), expectedCastIndex,
                $"10s 内 B 未收到 A 的第 {expectedCastIndex + 1} 条 DamageResult"
                + $"（施法被拒？位置/朝向未上报？锥外？技能 {entry.SkillId}）");
        }

        private static void DriveOneFrame(Rig rig) => DriveOneFrame(rig.A, rig.B);

        private static void DriveOneFrame(Ctx a, Ctx b)
        {
            a.Manager.Update(Time.deltaTime);
            b.Manager.Update(Time.deltaTime);
        }

        /// <summary>等 A 收到自己第 <paramref name="expectedCastIndex"/>+1 条命中反馈（独立下行发送，
        /// 与 B 的副本有帧级时差）。</summary>
        private static IEnumerator AwaitCasterCopy(Rig rig, long casterId, int expectedCastIndex)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (CountCasts(rig.A.Damages, casterId) <= expectedCastIndex
                   && Time.realtimeSinceStartup < deadline)
            {
                DriveOneFrame(rig);
                yield return null;
            }
            Assert.Greater(CountCasts(rig.A.Damages, casterId), expectedCastIndex,
                $"A（caster）未收到第 {expectedCastIndex + 1} 条命中反馈"
                + $"（已收到 {CountCasts(rig.A.Damages, casterId)} 条）");
        }

        /// <summary>第 <paramref name="index"/> 条来自 <paramref name="casterId"/> 的伤害结果
        ///（按来源过滤——服务器上其它实体/怪物的伤害结果不污染索引）。</summary>
        private static DamageResult? FindCast(List<DamageResult> results, long casterId, int index)
        {
            int seen = 0;
            foreach (var r in results)
            {
                if (r.SourcePlayerId != casterId) continue;
                if (seen++ == index) return r;
            }
            return null;
        }

        private static int CountCasts(List<DamageResult> results, long casterId)
        {
            int count = 0;
            foreach (var r in results)
                if (r.SourcePlayerId == casterId) count++;
            return count;
        }

        /// <summary>伤害必须落在服务端掷骰域内：{BaseDamage, BaseDamage×CritMultiplier} ×（格挡 ? Scale : 1）。
        /// 表值从 CombatRules 读——不写死 50/75/15/22.5。</summary>
        private static void AssertDamageInDomain(float damage, SkillEntry entry, bool blocked, string label)
        {
            float scale = blocked ? CombatRules.BlockDamageScale : 1f;
            float min = entry.BaseDamage * scale;
            float max = entry.BaseDamage * CombatRules.CritMultiplier * scale;
            bool ok = Mathf.Abs(damage - min) < 0.01f || Mathf.Abs(damage - max) < 0.01f;
            Assert.IsTrue(ok,
                $"{label}：伤害 {damage} 不在服务端掷骰域 {{{min}, {max}}} 内"
                + $"（base {entry.BaseDamage} ×(crit {CombatRules.CritMultiplier})? × block {scale}）");
        }
    }
}
