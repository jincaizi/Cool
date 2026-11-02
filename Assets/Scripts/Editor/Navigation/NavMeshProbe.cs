using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace GameSys.EditorNavigation
{
    public static class NavMeshProbe
    {
        public const string DemoDayScenePath = "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity";

        [MenuItem("Tools/Navigation/Probe NavMesh")]
        public static void Probe()
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            int triangles = tri.indices == null ? 0 : tri.indices.Length / 3;
            Debug.Log($"[NavMeshProbe] vertices={tri.vertices?.Length ?? 0} triangles={triangles} areas={tri.areas?.Length ?? 0}");
            if (triangles == 0)
            {
                Debug.LogError("[NavMeshProbe] 没有取到 NavMesh。确认 DemoDay 已打开、NavMeshSurface 处于启用状态。");
                return;
            }

            var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            var max = new Vector3(float.MinValue, 0f, float.MinValue);
            foreach (var v in tri.vertices)
            {
                min.x = Mathf.Min(min.x, v.x); min.z = Mathf.Min(min.z, v.z);
                max.x = Mathf.Max(max.x, v.x); max.z = Mathf.Max(max.z, v.z);
            }
            Debug.Log($"[NavMeshProbe] bounds x[{min.x:F1},{max.x:F1}] z[{min.z:F1},{max.z:F1}]");
        }
    }
}
