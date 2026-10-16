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
        public void ValidateShape_Rejects_NaN_Range()
        {
            // 防 NaN 绕过：NaN 比较恒 false，可穿透全部上限检查
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var nan = new HitShape { ShapeType = 0, Range = float.NaN, Angle = 120f };
            Assert.IsFalse(CombatRules.ValidateShape(nan, q));
        }

        [Test]
        public void ValidateShape_Rejects_NaN_Angle()
        {
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var nan = new HitShape { ShapeType = 0, Range = 3.5f, Angle = float.NaN };
            Assert.IsFalse(CombatRules.ValidateShape(nan, q));
        }

        [Test]
        public void ValidateShape_Rejects_Negative_Range()
        {
            // 负值只会削弱自己，但固化不变量：合法形状各字段非负
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var neg = new HitShape { ShapeType = 0, Range = -5f, Angle = 120f };
            Assert.IsFalse(CombatRules.ValidateShape(neg, q));
        }

        [Test]
        public void ValidateShape_Accepts_Exact_Boundary_Values()
        {
            // 含边界语义：恰好等于表上限不被拒
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            var edge = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f };
            Assert.IsTrue(CombatRules.ValidateShape(edge, q));

            Assert.IsTrue(CombatRules.TryGet(20002, out var r));
            var edgeR = new HitShape { ShapeType = 1, AreaRadius = 2.5f };
            Assert.IsTrue(CombatRules.ValidateShape(edgeR, r));
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

        [Test]
        public void Monster_Skill_Entries_Are_Synced_And_Validated()
        {
            Assert.IsTrue(CombatRules.IsSynced(50001));
            Assert.IsTrue(CombatRules.IsSynced(50002));

            Assert.IsTrue(CombatRules.TryGet(50001, out var slime));
            Assert.AreEqual(1.5f, slime.MaxRange);
            Assert.AreEqual(10f, slime.BaseDamage);
            Assert.AreEqual(0f, slime.CriticalRate);

            Assert.IsTrue(CombatRules.TryGet(50002, out var wolf));
            Assert.AreEqual(2f, wolf.MaxRange);
            Assert.AreEqual(15f, wolf.BaseDamage);

            Assert.IsTrue(CombatRules.ValidateShape(new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f }, slime));
            Assert.IsFalse(CombatRules.ValidateShape(new HitShape { ShapeType = 0, Range = 1.6f, Angle = 120f }, slime));

            Assert.AreEqual(15u, CombatRules.CooldownTicks(wolf)); // 1s @15Hz
        }

        [Test]
        public void HitDelay_Ticks_Map_From_Asset_Hit_Frames_At_15Hz()
        {
            Assert.IsTrue(CombatRules.TryGet(10001, out var combo));
            Assert.AreEqual(0.30f, combo.HitDelaySeconds, 0.0001f);
            Assert.AreEqual(5U, CombatRules.HitDelayTicks(combo));

            Assert.IsTrue(CombatRules.TryGet(10002, out var heavy));
            Assert.AreEqual(0.35f, heavy.HitDelaySeconds, 0.0001f);
            Assert.AreEqual(6U, CombatRules.HitDelayTicks(heavy));

            Assert.IsTrue(CombatRules.TryGet(20002, out var spin));
            Assert.AreEqual(0f, spin.HitDelaySeconds, 0.0001f);
            Assert.AreEqual(0U, CombatRules.HitDelayTicks(spin));
        }

        [Test]
        public void SkillQ_HitDelay_Is_Pinned_To_The_Asset_Measured_Value()
        {
            // 与服务器侧同构的回归钉：20001 是资产实测值（0.30s），非怪物占位 0.35s——
            // "≤ 冷却"不等式允许把它降级成 0.03 也全绿。
            Assert.IsTrue(CombatRules.TryGet(20001, out var q));
            Assert.AreEqual(0.30f, q.HitDelaySeconds, 0.0001f);
            Assert.AreEqual(5U, CombatRules.HitDelayTicks(q));
        }

        [Test]
        public void Monster_HitDelay_Never_Exceeds_Its_Cooldown()
        {
            foreach (var skillId in new[] { 50001, 50002, 50003, 50004 })
            {
                Assert.IsTrue(CombatRules.TryGet(skillId, out var entry), $"缺 {skillId} 表项");
                Assert.LessOrEqual(entry.HitDelaySeconds, entry.CooldownSeconds + 0.0001f,
                    $"skill {skillId} hitDelay > cooldown");
            }
        }
    }
}
