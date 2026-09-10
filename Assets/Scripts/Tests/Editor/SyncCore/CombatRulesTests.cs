using Cool.Protocol;
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class CombatRulesTests
    {
        [Test]
        public void Table_Covers_P4_Synced_Skills()
        {
            Assert.IsTrue(CombatRules.IsSynced(10001));
            Assert.IsTrue(CombatRules.IsSynced(10002));
            Assert.IsTrue(CombatRules.IsSynced(20001));
            Assert.IsTrue(CombatRules.IsSynced(20002));
            Assert.IsFalse(CombatRules.IsSynced(20003)); // Fireball 投射物：P4 不进表（D-P4-1）
            Assert.IsFalse(CombatRules.IsSynced(30001)); // Ultimate
            Assert.IsFalse(CombatRules.IsSynced(999999));
        }

        [Test]
        public void Table_Values_Match_Client_Assets()
        {
            // 2026-09-11 资产实测：BA_Combo1/BA_Heavy/Instant_SkillQ/Spin_SkillR（决策表）
            Assert.IsTrue(CombatRules.TryGet(10001, out var combo));
            Assert.AreEqual(0, combo.ShapeType);
            Assert.AreEqual(2f, combo.MaxRange, 0.0001f);
            Assert.AreEqual(0f, combo.CooldownSeconds, 0.0001f);
            Assert.AreEqual(50f, combo.BaseDamage, 0.0001f);
            Assert.AreEqual(0.15f, combo.CriticalRate, 0.0001f);

            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            Assert.AreEqual(0, q.ShapeType);
            Assert.AreEqual(3.5f, q.MaxRange, 0.0001f);
            Assert.AreEqual(6f, q.CooldownSeconds, 0.0001f);
            Assert.AreEqual(120f, q.BaseDamage, 0.0001f);

            Assert.IsTrue(CombatRules.TryGet(20002, out var r));
            Assert.AreEqual(1, r.ShapeType); // AOE_Circle
            Assert.AreEqual(2.5f, r.MaxAreaRadius, 0.0001f);
            Assert.AreEqual(5f, r.CooldownSeconds, 0.0001f);
            Assert.AreEqual(160f, r.BaseDamage, 0.0001f);
        }

        [Test]
        public void ValidateShape_Accepts_Asset_Params()
        {
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var shape = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f, InnerRadius = 0.8f, InnerAngle = 90f };
            Assert.IsTrue(CombatRules.ValidateShape(shape, q));
        }

        [Test]
        public void ValidateShape_Rejects_Inflated_Range()
        {
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var cheated = new HitShape { ShapeType = 0, Range = 999f, Angle = 120f };
            Assert.IsFalse(CombatRules.ValidateShape(cheated, q));
        }

        [Test]
        public void ValidateShape_Rejects_Wrong_Shape_Type()
        {
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var wrong = new HitShape { ShapeType = 1, Range = 3.5f, AreaRadius = 3.5f, Angle = 120f };
            Assert.IsFalse(CombatRules.ValidateShape(wrong, q));
        }

        [Test]
        public void ValidateShape_Rejects_Inflated_AreaRadius()
        {
            Assert.IsTrue(CombatRules.TryGet(20002, out var r));
            var cheated = new HitShape { ShapeType = 1, AreaRadius = 50f, Angle = 120f };
            Assert.IsFalse(CombatRules.ValidateShape(cheated, r));
        }

        [Test]
        public void CooldownTicks_Rounds_Up_At_15Hz()
        {
            Assert.IsTrue(CombatRules.TryGet(10001, out var combo));
            Assert.AreEqual(0U, CombatRules.CooldownTicks(combo));    // 0s → 无冷却
            Assert.IsTrue(CombatRules.TryGet(10002, out var heavy));
            Assert.AreEqual(8U, CombatRules.CooldownTicks(heavy));    // 0.5s × 15 = 7.5 → 8
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            Assert.AreEqual(90U, CombatRules.CooldownTicks(q));       // 6s × 15
            Assert.IsTrue(CombatRules.TryGet(20002, out var r));
            Assert.AreEqual(75U, CombatRules.CooldownTicks(r));       // 5s × 15
        }
    }
}
