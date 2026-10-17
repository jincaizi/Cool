using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class HitResolverTests
    {
        private static CombatCandidate C(long id, float x, float z, bool alive = true)
            => new CombatCandidate(id, new Vec3(x, 0f, z), alive);

        // Single 形状：SkillQ range 3.5 / angle 120（全角 → 半角 60°）
        private static readonly HitShape Single35 = new HitShape
        {
            ShapeType = 0, Range = 3.5f, Angle = 120f
        };

        private static readonly HitShape Circle25 = new HitShape
        {
            ShapeType = 1, AreaRadius = 2.5f
        };

        [Test]
        public void Single_Explicit_Target_In_Range_Hits()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 2f), C(3, 0f, 5f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Single_Explicit_Target_Exactly_At_Range_Hits()
        {
            // 距离恰为 Range（3.5，含边界）：dist == Range 不被拒
            var candidates = new List<CombatCandidate> { C(2, 0f, 3.5f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Single_Explicit_Target_Out_Of_Range_Misses()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 5f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.IsEmpty(hits);
        }

        [Test]
        public void Single_Explicit_Target_Dead_Misses()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 2f, alive: false) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.IsEmpty(hits);
        }

        [Test]
        public void Single_Explicit_Target_Behind_Caster_Misses()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, -2f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void Single_Explicit_Target_At_Cone_Edge_Hits()
        {
            var candidates = new List<CombatCandidate> { C(2, 0.8660254f, 0.5f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Single_Explicit_Target_Zero_Distance_Hits()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 0f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 2, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Single_AutoTarget_Picks_Nearest_In_Cone()
        {
            var candidates = new List<CombatCandidate> { C(2, 0f, 3f), C(3, 0f, 1f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 3 })); // 最近者
        }

        [Test]
        public void Single_AutoTarget_Ignores_Behind_Caster()
        {
            // 身后实体（-Z）在 range 内但不在前方 ±60° 锥内；前方实体命中
            var candidates = new List<CombatCandidate> { C(2, 0f, -2f), C(3, 0f, 2f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 3 }));
        }

        [Test]
        public void Single_AutoTarget_ZeroDistance_Degenerate_Hits()
        {
            // 双端同出生点（D-P4-2a 退化修正）：dist=0 视为锥内
            var candidates = new List<CombatCandidate> { C(2, 0f, 0f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Single_AutoTarget_Respects_Caster_Yaw()
        {
            // yaw=90° → forward=(1,0,0)（+X）；仅 +X 侧实体在锥内
            var candidates = new List<CombatCandidate> { C(2, 2f, 0f), C(3, 0f, 2f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 90f, 0, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Single_AutoTarget_Exactly_At_Cone_Edge_Hits()
        {
            // 锥边界恰 60°：(x,z)=(sin60°,cos60°) → dot == halfCos == 0.5f 位级相等（含边界语义）；
            // 该点 dist=1.0 在 Range 内，隔离验证锥含边界（仅差 1 ulp 靠外的点会被拒）
            var candidates = new List<CombatCandidate> { C(2, 0.8660254f, 0.5f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Single35, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void Circle_Hits_All_Within_Radius_Excludes_Far()
        {
            var candidates = new List<CombatCandidate> { C(2, 1f, 0f), C(3, 0f, 2f), C(4, 10f, 0f) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Circle25, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2, 3 }));
        }

        [Test]
        public void Circle_Ignores_Dead()
        {
            var candidates = new List<CombatCandidate> { C(2, 1f, 0f), C(3, 0f, 2f, alive: false) };
            var hits = HitResolver.Resolve(new Vec3(0, 0, 0), 0f, 0, Circle25, candidates);
            Assert.That(hits, Is.EqualTo(new long[] { 2 }));
        }

        [Test]
        public void test_line_all_hits_band_and_skips_outside()
        {
            // yaw=0 → +Z；线从 (0,0) 起、长 10、半宽 1
            var cands = new List<CombatCandidate>
            {
                C(1, 0f, 5f),    // 带内
                C(2, 0.9f, 2f),  // 带内（贴边）
                C(3, 1.2f, 5f),  // 带外
                C(4, 0f, -3f),   // 背后
                C(5, 0f, 11f),   // 超出长度
            };
            var hits = HitResolver.ResolveLineAll(new Vec3(0f, 0f, 0f), 0f, 0f, 10f, 1f, null, cands);
            CollectionAssert.AreEquivalent(new long[] { 1, 2 }, hits);
        }

        [Test]
        public void test_line_all_respects_banned_ids()
        {
            var cands = new List<CombatCandidate> { C(1, 0f, 5f), C(2, 0f, 3f) };
            var hits = HitResolver.ResolveLineAll(new Vec3(0f, 0f, 0f), 0f, 0f, 10f, 1f, new HashSet<long> { 1 }, cands);
            CollectionAssert.AreEqual(new long[] { 2 }, hits);
        }

        [Test]
        public void test_line_nearest_returns_only_nearest_in_band()
        {
            var cands = new List<CombatCandidate> { C(1, 0f, 8f), C(2, 0.5f, 3f), C(3, 3f, 1f) };
            var nearest = HitResolver.ResolveLineNearest(new Vec3(0f, 0f, 0f), 0f, 0f, 8f, 1f, null, cands, out var hitPos);
            Assert.AreEqual(2L, nearest);
            Assert.AreEqual(3f, hitPos.Z, 0.001f, "溅射中心 = 命中者位置");
        }

        [Test]
        public void test_circle_at_point_hits_inside_radius_only()
        {
            var cands = new List<CombatCandidate> { C(1, 3f, 0f), C(2, 4.5f, 0f), C(3, 0f, 4f) };
            var hits = HitResolver.ResolveCircle(new Vec3(0f, 0f, 0f), 4f, cands);
            CollectionAssert.AreEquivalent(new long[] { 1, 3 }, hits);
        }
    }
}
