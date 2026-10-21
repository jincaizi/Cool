using System.Linq;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>身体泛红目标筛选：SMR 全收；MeshRenderer 只收 Head*/Hair*。</summary>
    public class PlayerBodyTintTargetTests
    {
        [Test]
        public void test_body_target_includes_skinned_and_head_hair_mesh_renderers()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Character/FemalePlayer.prefab");
            Assert.IsNotNull(prefab, "FemalePlayer.prefab 未找到");

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Renderer Find(string name) => renderers.Single(r => r.gameObject.name == name);

            Assert.IsTrue(PlayerBodyTint.IsBodyTarget(Find("Body05")), "SMR 身体应参与泛红");
            Assert.IsTrue(PlayerBodyTint.IsBodyTarget(Find("Head01_Male")), "Head* MeshRenderer 应参与泛红");
            Assert.IsTrue(PlayerBodyTint.IsBodyTarget(Find("Hair01")), "Hair* MeshRenderer 应参与泛红");

            Assert.IsFalse(PlayerBodyTint.IsBodyTarget(Find("Eye01")), "眼睛不参与泛红");
            Assert.IsFalse(PlayerBodyTint.IsBodyTarget(Find("Mouth01")), "嘴不参与泛红");
            Assert.IsFalse(PlayerBodyTint.IsBodyTarget(Find("Backpack01")), "背包不参与泛红");
        }
    }
}
