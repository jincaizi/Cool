using Hotfix.GameSystems.Monster;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// P6 ⑤：刷怪记账回归测试。
    /// 原 bug：死亡按 MonsterId 记计时器，到期重生整组 Count 只 → 杀 1 只 30s 后 +11 只，只增不减。
    /// </summary>
    public class SpawnLedgerTests
    {
        private SpawnLedger _ledger;

        [SetUp]
        public void SetUp() => _ledger = new SpawnLedger();

        [Test]
        public void test_record_spawn_increments_group_alive_count()
        {
            // Arrange & Act
            for (int i = 0; i < 11; i++)
                _ledger.RecordSpawn(groupIndex: 0, entityId: 1000 + i, origin: Vector3.zero);

            // Assert
            Assert.AreEqual(11, _ledger.AliveCount(0));
        }

        [Test]
        public void test_one_death_removes_exactly_one_from_alive_count()
        {
            // Arrange（11 只一组）
            for (int i = 0; i < 11; i++)
                _ledger.RecordSpawn(0, 1000 + i, Vector3.zero);

            // Act（杀 1 只）
            bool recorded = _ledger.RecordDeath(entityId: 1000, out int groupIndex, out _);

            // Assert（原 bug 会让存活数反弹到 21）
            Assert.IsTrue(recorded, "死亡应被记账");
            Assert.AreEqual(0, groupIndex);
            Assert.AreEqual(10, _ledger.AliveCount(0), "死一只只能减一只");
        }

        [Test]
        public void test_death_returns_the_initial_spawn_origin_not_death_position()
        {
            // Arrange（怪在 (3,0,4) 生成，被玩家引到远处打死）
            var origin = new Vector3(3f, 0f, 4f);
            _ledger.RecordSpawn(0, 42, origin);

            // Act
            _ledger.RecordDeath(42, out _, out Vector3 respawnOrigin);

            // Assert（按死亡位置重生会让怪群逐步漂移出刷怪区）
            Assert.AreEqual(origin, respawnOrigin, "重生点必须是初始生成点");
        }

        [Test]
        public void test_death_of_unknown_entity_is_ignored()
        {
            // Arrange（未记录的实体）
            _ledger.RecordSpawn(0, 1, Vector3.zero);

            // Act
            bool recorded = _ledger.RecordDeath(entityId: 999, out _, out _);

            // Assert
            Assert.IsFalse(recorded, "未记录实体的死亡应被忽略，不能污染计数");
            Assert.AreEqual(1, _ledger.AliveCount(0));
        }

        [Test]
        public void test_alive_count_is_tracked_per_group()
        {
            // Arrange & Act
            _ledger.RecordSpawn(0, 1, Vector3.zero);
            _ledger.RecordSpawn(0, 2, Vector3.zero);
            _ledger.RecordSpawn(1, 3, Vector3.zero);

            // Assert
            Assert.AreEqual(2, _ledger.AliveCount(0));
            Assert.AreEqual(1, _ledger.AliveCount(1));
        }
    }
}
