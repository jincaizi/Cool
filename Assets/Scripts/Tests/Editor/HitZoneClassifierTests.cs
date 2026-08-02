using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class HitZoneClassifierTests
    {
        // bounds: center (0,0,0) size (1,2,1) → min.y=-1 max.y=1，ratio=(y+1)/2
        private static readonly Bounds Bounds = new Bounds(Vector3.zero, new Vector3(1f, 2f, 1f));

        // ── 分类边界 ──
        [Test]
        public void Classify_RatioAtHeadThreshold_ReturnsHead() =>
            Assert.AreEqual(HitZonePart.Head, HitZoneClassifier.Classify(new Vector3(0f, 0.5f, 0f), Bounds)); // ratio 0.75（含等于）

        [Test]
        public void Classify_RatioAboveHeadThreshold_ReturnsHead() =>
            Assert.AreEqual(HitZonePart.Head, HitZoneClassifier.Classify(new Vector3(0f, 0.6f, 0f), Bounds)); // ratio 0.8

        [Test]
        public void Classify_MidHeight_ReturnsTorso() =>
            Assert.AreEqual(HitZonePart.Torso, HitZoneClassifier.Classify(new Vector3(0f, 0f, 0f), Bounds)); // ratio 0.5

        [Test]
        public void Classify_RatioAtLegsThreshold_ReturnsLegs() =>
            Assert.AreEqual(HitZonePart.Legs, HitZoneClassifier.Classify(new Vector3(0f, -0.4f, 0f), Bounds)); // ratio 0.3

        [Test]
        public void Classify_Bottom_ReturnsLegs() =>
            Assert.AreEqual(HitZonePart.Legs, HitZoneClassifier.Classify(new Vector3(0f, -1f, 0f), Bounds)); // ratio 0

        [Test]
        public void Classify_ZeroHeightBounds_ReturnsTorso() =>
            Assert.AreEqual(HitZonePart.Torso,
                HitZoneClassifier.Classify(Vector3.zero, new Bounds(Vector3.zero, Vector3.zero)));

        // ── 倍率 ──
        [Test]
        public void KnockbackMultiplier_HeadIs15() =>
            Assert.AreEqual(1.5f, HitZoneClassifier.KnockbackMultiplier(HitZonePart.Head));

        [Test]
        public void KnockbackMultiplier_TorsoIsIdentity() =>
            Assert.AreEqual(1.0f, HitZoneClassifier.KnockbackMultiplier(HitZonePart.Torso));

        [Test]
        public void KnockbackMultiplier_LegsIs07() =>
            Assert.AreEqual(0.7f, HitZoneClassifier.KnockbackMultiplier(HitZonePart.Legs));

        [Test]
        public void ScaleMultiplier_HeadIs13() =>
            Assert.AreEqual(1.3f, HitZoneClassifier.ScaleMultiplier(HitZonePart.Head));

        [Test]
        public void ScaleMultiplier_TorsoIsIdentity() =>
            Assert.AreEqual(1.0f, HitZoneClassifier.ScaleMultiplier(HitZonePart.Torso));

        [Test]
        public void ScaleMultiplier_LegsIs08() =>
            Assert.AreEqual(0.8f, HitZoneClassifier.ScaleMultiplier(HitZonePart.Legs));

        // ── HitContact.None 防呆 ──
        [Test]
        public void None_HasNoContactAndIsTorso()
        {
            Assert.IsFalse(HitContact.None.HasContact);
            Assert.AreEqual(HitZonePart.Torso, HitContact.None.Part);
        }

        // ── FromDirection 方向重建（胶囊：center (0,1,0) height 2 radius 0.5 → 底 y=0 顶 y=2）──
        private static CapsuleCollider CreateCapsule()
        {
            var go = new GameObject("TestCapsule");
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.height = 2f;
            col.radius = 0.5f;
            return col;
        }

        [Test]
        public void FromDirection_SideHit_FindsSurfacePointAsTorso()
        {
            var col = CreateCapsule();
            try
            {
                var contact = HitContact.FromDirection(col, Vector3.right);
                Assert.IsTrue(contact.HasContact);
                Assert.Less(contact.HitPoint.x, 0f);            // 攻击向右（攻击者在左侧）→ 接触左侧表面
                Assert.AreEqual(HitZonePart.Torso, contact.Part); // 锚定 bounds.center（y=1）→ ratio 0.5 → Torso
            }
            finally { Object.DestroyImmediate(col.gameObject); }
        }

        [Test]
        public void FromDirection_AttackerBelow_ClassifiesLegs()
        {
            var col = CreateCapsule();
            try
            {
                var contact = HitContact.FromDirection(col, Vector3.up); // 攻击者在下方 → 接触底点
                Assert.AreEqual(HitZonePart.Legs, contact.Part);
            }
            finally { Object.DestroyImmediate(col.gameObject); }
        }

        [Test]
        public void FromDirection_AttackerAbove_ClassifiesHead()
        {
            var col = CreateCapsule();
            try
            {
                var contact = HitContact.FromDirection(col, Vector3.down); // 攻击者在上方 → 接触顶点
                Assert.AreEqual(HitZonePart.Head, contact.Part);
            }
            finally { Object.DestroyImmediate(col.gameObject); }
        }

        [Test]
        public void FromDirection_InvalidDirection_FallsBackToTorsoHeight()
        {
            var col = CreateCapsule();
            try
            {
                var contact = HitContact.FromDirection(col, Vector3.zero);
                Assert.IsTrue(contact.HasContact);
                Assert.AreEqual(HitZonePart.Torso, contact.Part);
            }
            finally { Object.DestroyImmediate(col.gameObject); }
        }
    }
}
