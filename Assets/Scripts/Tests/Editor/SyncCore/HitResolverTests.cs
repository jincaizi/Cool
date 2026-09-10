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
    }
}
