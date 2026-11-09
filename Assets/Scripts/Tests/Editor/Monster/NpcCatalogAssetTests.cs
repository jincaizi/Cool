using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameSys.EditorTests
{
    public sealed class NpcCatalogAssetTests
    {
        [Test]
        public void Published_Catalog_Exists_Before_Network_Login()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/Monstor/Network/MonsterCatalog.asset");
            Assert.That(asset, Is.Not.Null, "The network catalog must be a published asset, not a runtime fallback.");
        }
    }
}
