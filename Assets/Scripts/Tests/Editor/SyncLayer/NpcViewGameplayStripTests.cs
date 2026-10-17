using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 联网模式下「打怪就抛 NullReferenceException」的回归守卫（2026-10-17）。
    ///
    /// 原缺陷：npc 视图是拿**原始怪物预制体**实例化的，而 `SlimePBR` / `TurtleShellPBR` 身上
    /// 带着完整的 `MonsterEntity`（`IDamageable` + `IEffectTarget`）。视图路径只**追加**了表现
    /// 组件，没人调 `Init`，于是视图上那个实体的 `_stats` 是 null。而技能判定走
    /// `Physics.OverlapSphereNonAlloc(shape.TargetMask)` + `TryGetComponent&lt;IEffectTarget&gt;()`
    /// ——不是注册表——所以视图那个 **enabled 的 CapsuleCollider** 让未初始化的实体成了合法目标：
    ///
    ///   SkillExecutor.ApplyDamage → IDamageable.TakeDamage → MonsterEntity.cs:174 `_stats.IsDead` → NRE
    ///
    /// 两条断言各钉一半：①视图不得参与伤害链（剥离）；②未初始化实体收到伤害不得抛（同帧窗口兜底）。
    /// DogKnight / Spider 预制体本来就不带 `MonsterEntity`——这正是该缺陷「常出现但不是必现」的原因，
    /// 也因此只在带玩法组件的两个模板上断言。
    /// </summary>
    public class NpcViewGameplayStripTests
    {
        /// <summary>与 DemoDay 场景的 `_npcPrefabs` 接线同款（templateId 1 → Slime、2 → TurtleShell）。
        /// 这两个是**挂 MonsterEntity 的**那两个预制体。</summary>
        private static readonly (string PrefabPath, int TemplateId)[] DamageablePrefabs =
        {
            ("Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/SlimePBR.prefab", 1),
            ("Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/TurtleShellPBR.prefab", 2),
        };

        [Test]
        public void test_npc_view_carries_no_damageable_and_keeps_its_presentation()
        {
            foreach (var (prefabPath, templateId) in DamageablePrefabs)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(prefab, "预制体缺失：" + prefabPath);
                // 前提断言：没有这个组件，本用例就抓不到原缺陷（预制体被改线时当场报出来）
                Assert.IsNotNull(prefab!.GetComponentInChildren<MonsterEntity>(true),
                    prefabPath + " 应带 MonsterEntity —— 它正是原缺陷里那个未初始化就被打的实体");

                var root = new GameObject("NpcStripRoot");
                var registry = new RemoteEntityRegistry(root.transform, null,
                    new Dictionary<int, GameObject> { { templateId, prefab } });
                try
                {
                    long npcId = 2_000_000L + templateId;
                    registry.ApplySnapshot(new EntitySnapshot
                    {
                        PlayerId = npcId,
                        FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                            | EntityFieldMask.TemplateId),
                        X = 1f, Y = 0f, Z = 2f,
                        Kind = (byte)EntityKind.Npc, TemplateId = templateId,
                        Tick = 1
                    }, serverTicks: 638_000_000_000_000_000L);

                    var view = registry.GetView(npcId);
                    Assert.IsNotNull(view, "Npc 视图未创建");
                    var go = view!.GameObject;

                    // ① 视图不参与伤害链：本地技能的物理查询与 TryGetComponent 都够不着它
                    Assert.IsNull(go.GetComponentInChildren<IDamageable>(true),
                        "npc 视图上不得存在 IDamageable —— 存在即可被本地技能命中（原缺陷：未初始化的 MonsterEntity 在 TakeDamage 第一句就 NRE）");
                    Assert.IsNull(go.GetComponentInChildren<IEffectTarget>(true),
                        "npc 视图上不得存在 IEffectTarget —— 同上，技能判定就是按这个接口收集目标的");

                    // ② 但表现必须留着：剥离不得把模型/动画一起拆掉
                    Assert.IsNotNull(go.GetComponentInChildren<Animator>(true),
                        "视图必须保留模型的 Animator（剥离只针对玩法组件）");
                    Assert.IsNotNull(view.NpcAnimator,
                        "视图必须保留 NpcEntityAnimator 驱动（剥离只针对玩法组件）");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        public void test_uninitialized_monster_entity_does_not_throw_on_damage()
        {
            var go = new GameObject("UninitializedMonster");
            go.AddComponent<UnityEngine.AI.NavMeshAgent>();
            go.AddComponent<Animator>();
            var entity = go.AddComponent<MonsterEntity>();
            try
            {
                var damageable = (IDamageable)entity;

                // 未 Init ⇒ 不可作为目标（同文件 ITargetable 的 HP 系列本来就在 null 守卫）。
                // 读值包在 DoesNotThrow 里：这条的失败应当是「抛了」，不是「报错」。
                bool alive = true;
                Assert.DoesNotThrow(() => { alive = damageable.IsAlive; },
                    "未 Init 的实体读 IsAlive 抛了 —— 与 TakeDamage 同源（_stats 未守卫）");
                Assert.IsFalse(alive, "未 Init 的实体不得报活 —— 报活就会被选成目标");

                // 剥离是「先禁再销毁」，销毁帧末才生效 ⇒ 创建帧内仍可能被同帧命中。
                // `enabled = false` 挡不住接口调用，所以这里必须有守卫。
                Assert.DoesNotThrow(
                    () => damageable.TakeDamage(new DamageBlock(), Vector3.forward),
                    "未 Init 的实体收到伤害必须安静返回 —— 原缺陷就是 MonsterEntity.cs:174 这一句 NRE");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
