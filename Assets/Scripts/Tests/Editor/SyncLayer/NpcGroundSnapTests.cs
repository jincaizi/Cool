using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// Npc 视图落地吸附：服务端布局是 2D 的（y 恒 0），客户端把怪贴到地形面（见 RemoteEntityRegistry.GroundY）。
    ///
    /// **不为测试建地形**：注入一块 BoxCollider 板当"地形根"（<see cref="RemoteEntityRegistry.TerrainRoot"/>），
    /// 测的是采样逻辑本身——只认地形（不认头顶的树冠）、采样复用、未命中回退。真地形（LowpolyTerrain）
    /// 在 DemoDay 里，由 Play 模式探针实测（production/verify/npc-ground-snap.md）。
    /// </summary>
    public class NpcGroundSnapTests
    {
        private GameObject _root = null!;
        private GameObject? _stub;
        private GameObject? _occluder;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("NpcGroundSnapTestRoot");
            _registry = new RemoteEntityRegistry(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Clear();
            Object.DestroyImmediate(_root);
            if (_stub != null) Object.DestroyImmediate(_stub);
            if (_occluder != null) Object.DestroyImmediate(_occluder);
        }

        /// <summary>地形桩：200×1×200 的板，**顶面在 topY**（铺满测试区，任意 XZ 都能命中）。</summary>
        private static GameObject Slab(float topY)
        {
            var go = new GameObject("NpcGroundSnapTests_Slab");
            go.transform.position = new Vector3(0f, topY - 0.5f, 0f);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(200f, 1f, 200f);
            Physics.SyncTransforms();   // EditMode 默认不自动同步变换（autoSyncTransforms = 0）
            return go;
        }

        private static EntitySnapshot Snap(long id, float x, float z, byte kind, float y = 0f)
            => new EntitySnapshot
            {
                PlayerId = id,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                X = x, Y = y, Z = z, Kind = kind, Tick = 1
            };

        private void Render(long id, float x, float z, byte kind, float y = 0f)
        {
            _registry.ApplySnapshot(Snap(id, x, z, kind, y), serverTicks: 1_000_000L);
            _registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);
        }

        [Test]
        public void Npc_View_Is_Snapped_Onto_The_Terrain_Surface()
        {
            _stub = Slab(2.25f);
            _registry.TerrainRoot = _stub.transform;

            Render(1, 0f, 0f, (byte)EntityKind.Npc);   // 服务端给的 y = 0（2D 布局）

            Assert.AreEqual(2.25f, _registry.GetView(1)!.Position.y, 1e-3f,
                "Npc 视图的 y 应换成地面高度（x/z 不动）");
        }

        [Test]
        public void Player_And_Bot_Views_Keep_Their_Server_Y()
        {
            _stub = Slab(2.25f);
            _registry.TerrainRoot = _stub.transform;

            Render(2, 0f, 0f, (byte)EntityKind.Player);
            Render(3, 0f, 0f, (byte)EntityKind.PlayerBot);

            Assert.AreEqual(0f, _registry.GetView(2)!.Position.y, 1e-3f,
                "真人视图不吸附（客户端权威，y 由本地物理决定）");
            Assert.AreEqual(0f, _registry.GetView(3)!.Position.y, 1e-3f,
                "bot 与真人走同一条路径，同样不吸附");
        }

        [Test]
        public void Canopy_Above_The_Terrain_Does_Not_Win()
        {
            // 头顶的遮挡体（树冠的模型）：与地形同层、比地形离射线起点更近
            _stub = Slab(2.25f);
            _occluder = Slab(12f);
            _occluder.name = "NpcGroundSnapTests_TreeCanopy";
            _registry.TerrainRoot = _stub.transform;

            Render(4, 0f, 0f, (byte)EntityKind.Npc);

            Assert.AreEqual(2.25f, _registry.GetView(4)!.Position.y, 1e-3f,
                "被树冠截胡时必须继续认地形（不加身份校验就会吸到 12m 高的树冠上）");
        }

        [Test]
        public void Miss_Keeps_Server_Y_Instead_Of_Zero()
        {
            // 没有地形的情形（测试宿主/探针场景）：地形根存在，但没有可命中的地形面
            _stub = new GameObject("NpcGroundSnapTests_EmptyRoot");
            _registry.TerrainRoot = _stub.transform;

            Assert.DoesNotThrow(() =>
                Render(5, 0f, 0f, (byte)EntityKind.Npc, y: 0.4f), "没有地形也不许抛");

            Assert.AreEqual(0.4f, _registry.GetView(5)!.Position.y, 1e-3f,
                "未命中 ⇒ 保持服务端 y——回退 0 会把没有地形的区域里的怪压到地下");
        }

        [Test]
        public void Samples_Are_Reused_Until_The_Npc_Moves_Past_The_Radius()
        {
            _stub = Slab(2.25f);
            _registry.TerrainRoot = _stub.transform;
            Assert.AreEqual(2.25f, _registry.GroundY(7, 0f, 0f, 9f), 1e-3f, "首次采样 = 地形面");

            // 地形"动"了（真地形是静态的，这里只为暴露复用是否真的生效）
            _stub.transform.position = new Vector3(0f, 5.25f - 0.5f, 0f);
            Physics.SyncTransforms();

            Assert.AreEqual(2.25f, _registry.GroundY(7, 0f, 0f, 9f), 1e-3f,
                "原处不动 ⇒ 复用上次采样，不再射线");
            Assert.AreEqual(5.25f, _registry.GroundY(7, 0.5f, 0f, 9f), 1e-3f,
                "挪出复用半径 ⇒ 重新采样");
        }
    }
}
