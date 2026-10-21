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

        private static SkinnedMeshRenderer AddBody(GameObject go, Material material, Mesh mesh)
        {
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterial = material;
            return smr;
        }

        private static MaterialPropertyBlock Block(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block;
        }

        private static void AssertTinted(Renderer renderer, string message)
        {
            var c = Block(renderer).GetColor("_Color");
            Assert.Greater(c.r, c.g, message);
        }

        private static void DestroyFixture(GameObject go, Material material, Mesh mesh)
        {
            Object.Destroy(go);
            Object.Destroy(material);
            Object.Destroy(mesh);
        }

        [UnityTest]
        public IEnumerator test_body_tint_hit_event_applies_red()
        {
            var material = new Material(Shader.Find("Standard"));
            var mesh = new Mesh();
            var go = new GameObject("TintTarget");
            var smr = AddBody(go, material, mesh);
            var tint = go.AddComponent<PlayerBodyTint>();
            tint.Bind(new FakeHpSource { HpPercent = 1f });

            EventBus.Emit(new PlayerTakeDamageEvent(go.GetInstanceID(), Vector3.zero,
                HitZonePart.Torso, Vector3.zero, 10, Color.white));
            yield return null;

            AssertTinted(smr, "受击泛红应提高红通道");

            DestroyFixture(go, material, mesh);
        }

        [UnityTest]
        public IEnumerator test_body_tint_hit_event_tints_head_mesh_renderer_and_smr()
        {
            var material = new Material(Shader.Find("Standard"));
            var mesh = new Mesh();
            var go = new GameObject("TintTarget");
            var smr = AddBody(go, material, mesh);

            var head = new GameObject("Head01");
            head.transform.SetParent(go.transform, false);
            var headRenderer = head.AddComponent<MeshRenderer>();
            headRenderer.sharedMaterial = material;

            var tint = go.AddComponent<PlayerBodyTint>();
            tint.Bind(new FakeHpSource { HpPercent = 1f });

            EventBus.Emit(new PlayerTakeDamageEvent(go.GetInstanceID(), Vector3.zero,
                HitZonePart.Torso, Vector3.zero, 10, Color.white));
            yield return null;

            AssertTinted(smr, "SMR 身体应泛红");
            AssertTinted(headRenderer, "Head01 MeshRenderer 应随身体一起泛红");

            DestroyFixture(go, material, mesh);
        }

        [UnityTest]
        public IEnumerator test_body_tint_low_hp_clears_after_recovery()
        {
            var material = new Material(Shader.Find("Standard"));
            var mesh = new Mesh();
            var go = new GameObject("TintTarget");
            var smr = AddBody(go, material, mesh);
            var tint = go.AddComponent<PlayerBodyTint>();
            var hp = new FakeHpSource { HpPercent = 0.1f };
            tint.Bind(hp);
            yield return null;

            AssertTinted(smr, "HP<20% 应持续泛红");

            hp.HpPercent = 0.5f;
            yield return null;

            Assert.IsTrue(Block(smr).isEmpty, "回到阈值以上应清除染色");

            DestroyFixture(go, material, mesh);
        }

        [UnityTest]
        public IEnumerator test_body_tint_zero_hp_dead_keeps_block_empty()
        {
            var material = new Material(Shader.Find("Standard"));
            var mesh = new Mesh();
            var go = new GameObject("TintTarget");
            var smr = AddBody(go, material, mesh);
            var tint = go.AddComponent<PlayerBodyTint>();
            tint.Bind(new FakeHpSource { HpPercent = 0f, IsDead = true });
            yield return null;

            Assert.IsTrue(Block(smr).isEmpty, "死亡（HP=0, IsDead）不应泛红");

            DestroyFixture(go, material, mesh);
        }

        [UnityTest]
        public IEnumerator test_body_tint_zero_hp_alive_keeps_block_empty()
        {
            var material = new Material(Shader.Find("Standard"));
            var mesh = new Mesh();
            var go = new GameObject("TintTarget");
            var smr = AddBody(go, material, mesh);
            var tint = go.AddComponent<PlayerBodyTint>();
            tint.Bind(new FakeHpSource { HpPercent = 0f, IsDead = false });
            yield return null;

            Assert.IsTrue(Block(smr).isEmpty, "HP=0 尚未初始化/刚归零时不应泛红");

            DestroyFixture(go, material, mesh);
        }
    }
}
