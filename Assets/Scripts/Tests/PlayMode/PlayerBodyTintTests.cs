using System.Collections;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class PlayerBodyTintTests
    {
        private sealed class FakeHpSource : IPlayerHpSource
        {
            public float HpPercent { get; set; }
            public bool IsDead { get; set; }
        }

        [UnityTest]
        public IEnumerator test_body_tint_hit_event_applies_red()
        {
            var go = new GameObject("TintTarget");
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = new Mesh();
            smr.sharedMaterial = new Material(Shader.Find("Standard"));
            var tint = go.AddComponent<PlayerBodyTint>();
            tint.Bind(new FakeHpSource { HpPercent = 1f });

            EventBus.Emit(new PlayerTakeDamageEvent(go.GetInstanceID(), Vector3.zero,
                HitZonePart.Torso, Vector3.zero, 10, Color.white));
            yield return null;

            var block = new MaterialPropertyBlock();
            smr.GetPropertyBlock(block);
            var c = block.GetColor("_Color");
            Assert.Greater(c.r, c.g, "受击泛红应提高红通道");

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator test_body_tint_low_hp_clears_after_recovery()
        {
            var go = new GameObject("TintTarget");
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = new Mesh();
            smr.sharedMaterial = new Material(Shader.Find("Standard"));
            var tint = go.AddComponent<PlayerBodyTint>();
            var hp = new FakeHpSource { HpPercent = 0.1f };
            tint.Bind(hp);
            yield return null;

            var block = new MaterialPropertyBlock();
            smr.GetPropertyBlock(block);
            Assert.Greater(block.GetColor("_Color").r, block.GetColor("_Color").g,
                "HP<20% 应持续泛红");

            hp.HpPercent = 0.5f;
            yield return null;

            block = new MaterialPropertyBlock();
            smr.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, "回到阈值以上应清除染色");

            Object.Destroy(go);
        }
    }
}
