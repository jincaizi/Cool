using System.Collections;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    /// <summary>
    /// P6 ⑦：单机本地死亡/复活闭环。HP 归零必须进 Death 态并锁移动，
    /// 倒计时结束后回满 HP、回 Base 层、回到出生点。
    /// </summary>
    public class PlayerDeathRespawnTests
    {
        /// <summary>
        /// 致死伤害：> maxHP(100) 且 < 9999。
        /// 不能用 9999——它会命中 HitFSM.EnterHit 的秒杀旁路（HitFSM.cs:212
        /// `Damage >= 9999 → HitState.Death`），那条路绕开 LockState（D-P6-4 明确禁止），
        /// 会让测试"看起来通过"却完全没验证 EvaluateLocalDeath / EnterDeath（D-P6-22）。
        /// </summary>
        private const float LethalDamage = 150f;

        /// <summary>测试用出生点：与实例化位置(原点)不同，才能证明复活确实发生了传送。</summary>
        private static readonly Vector3 TestSpawnPoint = new Vector3(5f, 0f, 5f);

        private GameObject _goInstance;
        private Sys3CEntry _entry;
        private NetworkMode _previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // EvaluateLocalDeath 有联网守卫（D-P6-18）。NetworkModeService 读 PlayerPrefs，
            // 之前的操作可能残留 Online 造成假失败——显式钉成单机，TearDown 还原。
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab,
                "测试用玩家 Prefab 缺失：需在 Assets/Resources/ 放置含 Sys3CEntry 的 Prefab 并命名 PlayerEntryForTest");
            _goInstance = Object.Instantiate(prefab);
            _entry = _goInstance.GetComponent<Sys3CEntry>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_goInstance != null) Object.DestroyImmediate(_goInstance);
        }

        [UnityTest]
        public IEnumerator test_hp_zero_enters_death_state()
        {
            // Arrange & Act（服务端伤害入口直接扣到 0；单机模式下走同一 HP 字段）
            _entry.ApplyServerDamage(LethalDamage, Vector3.back);
            yield return null;

            // Assert
            Assert.AreEqual(HitState.Death, _entry.Controller.HitFSM.CurrentState,
                "HP 归零必须进入 Death 态——曾什么都不发生，玩家仍能移动施法");
        }

        /// <summary>
        /// 真实致死入口：单机实际打玩家的路径是怪物
        /// `MonsterAttackState.ResolveDamage` → `IDamageable.TakeDamage(DamageBlock, dir)`
        /// （MonsterAttackState.cs:82 的 `t.TakeDamage(damage, dir)`）。
        /// 本文件其余用例都走 `ApplyServerDamage`（联网 DamageResult 入口），单机这条链上
        /// 挂着 HitZoneClassifier 部位倍率、防御修正器与 EvaluateLocalDeath，此前无人覆盖。
        /// </summary>
        [UnityTest]
        public IEnumerator test_monster_damage_entry_kills_the_player()
        {
            // Arrange（史莱姆的实弹：BaseDamage 10 / KnockbackForce 1.5；玩家 100 HP → 10 击）
            var slimeDamage = DamageBlock.CreateDefault(10f);
            slimeDamage.KnockbackForce = 1.5f;

            // Act（怪物每次出手结算一次命中）
            for (int i = 0; i < 10; i++)
                ((IDamageable)_entry).TakeDamage(slimeDamage, Vector3.back);
            yield return null;

            // Assert
            Assert.AreEqual(0f, _entry.CurrentHp, 0.01f, "10 击 × 10 伤害应打空 100 HP");
            Assert.IsTrue(_entry.IsDead, "经 IDamageable.TakeDamage 致死必须进入死亡状态");
            Assert.AreEqual(HitState.Death, _entry.Controller.HitFSM.CurrentState,
                "单机致死链（怪物 → TakeDamage → EvaluateLocalDeath → EnterDeath）必须走到 Death 态");
        }

        [UnityTest]
        public IEnumerator test_dead_player_has_movement_locked()
        {
            // Arrange & Act
            _entry.ApplyServerDamage(LethalDamage, Vector3.back);
            yield return null;

            // Assert（验收表第 5 条："HP 归零后……不能移动"）
            // 断言的是移动代码真正读的那个锁（CharacterController.ApplyHorizontalMovement
            // 只看 _cc.LockMovement），不是无消费者的 StateCoordinator.CanMove。
            Assert.IsTrue(_entry.IsDead, "应处于死亡状态");
            Assert.IsTrue(_entry.CoreController.LockMovement,
                "死亡时必须锁定移动——StateCoordinator.CanMove 全项目无消费者，靠 _isDead 显式并入");
        }

        [UnityTest]
        public IEnumerator test_respawn_restores_hp_and_layer()
        {
            // Arrange
            float maxHp = _entry.MaxHp;
            _entry.SpawnPoint = TestSpawnPoint;
            _entry.ApplyServerDamage(LethalDamage, Vector3.back);
            yield return null;

            // Act（等待复活倒计时 3s + 缓冲）
            yield return new WaitForSeconds(3.5f);

            // Assert
            Assert.AreEqual(maxHp, _entry.CurrentHp, 0.01f, "复活应回满 HP");
            Assert.AreEqual(HitState.None, _entry.Controller.HitFSM.CurrentState, "复活应回到 None 态");
            // _isDead 归假后两处门禁自动让位，无需额外"解锁"代码
            Assert.IsFalse(_entry.CoreController.LockMovement, "复活后死亡移动锁应自动解除");
            Assert.AreEqual(Hotfix.GameSystems.Sys3C.Core.LayerType.Base,
                _entry.Controller.Coordinator.ActiveLayer, "复活应把活动层交还 Base");

            // 复活应回到出生点。只判水平：测试场景无地面，角色随后自由落体，Y 会继续跑掉。
            Vector3 position = _entry.transform.position;
            Assert.AreEqual(TestSpawnPoint.x, position.x, 0.5f, "复活应回到出生点 X");
            Assert.AreEqual(TestSpawnPoint.z, position.z, 0.5f, "复活应回到出生点 Z");
        }
    }
}
