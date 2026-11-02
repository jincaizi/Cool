using System.IO;
using GameSys.EditorNavigation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Hotfix.GameSystems.Tests.Navigation
{
    public class NavMeshExportFreshnessTests
    {
        [Test]
        public void Committed_NavBin_Matches_Current_DemoDay_NavMesh()
        {
            // EditMode 测试运行时 Test Framework 会新建空场景并设为 active；
            // 这里临时切到 DemoDay，结束后恢复，避免新鲜度检查永远 Ignore。
            Scene original = SceneManager.GetActiveScene();
            Scene demo = SceneManager.GetSceneByPath(NavMeshExporter.DemoDayScenePath);
            bool openedHere = false;
            if (!demo.IsValid())
            {
                demo = EditorSceneManager.OpenScene(NavMeshExporter.DemoDayScenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                SceneManager.SetActiveScene(demo);

                string path = Path.Combine(Directory.GetCurrentDirectory(), NavMeshExporter.OutputPath);
                Assert.IsTrue(File.Exists(path), "Run Tools/Navigation/Export Server NavMesh first.");

                ulong freshHash = NavMeshExporter.ComputeCanonicalHashForActiveScene();
                ulong committedHash = NavMeshExporter.ReadCanonicalHash(path);
                Assert.AreEqual(freshHash, committedHash,
                    "Committed navbin is stale. Re-export with Tools/Navigation/Export Server NavMesh.");

                // 场景改动但 navmesh 没重烘：给 warning，不硬红（避免灯光等无关改动噪音）
                if (NavMeshExporter.ReadSourceHash(path) != NavMeshExporter.ComputeSourceHashForActiveScene())
                    UnityEngine.Debug.LogWarning("DemoDay scene changed since the navbin was exported; rebake the navmesh if geometry changed.");
            }
            finally
            {
                if (original.IsValid()) SceneManager.SetActiveScene(original);
                if (openedHere) EditorSceneManager.CloseScene(demo, true);
            }
        }
    }
}
