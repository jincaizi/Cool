using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 任务 10：联网目标锁定。筛选纯函数 + 真实 registry 上的锁定/清锁/施法前取目标行为。
    /// </summary>
    public class NetworkTargetSelectorTests
    {
        private const string ComboSkillPath = "Assets/PreRes/SkillsCfg/BA_Combo1.asset";
        private const string SpiderPrefab = "Assets/Monstor/fantasySpider/SpiderMonster.prefab";
        private const long LocalId = 1L;
        private const long ServerTicks = 1_000_000L;

        private GameObject _root = null!;
        private RemoteEntityRegistry _registry = null!;
        private GameObject _owner = null!;
        private InputManager _input = null!;
        private NetworkTargetSelector _selector = null!;

        private NetworkTargetable? _bound;
        private int _bindCalls;
        private int _clearCalls;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SelectorTestRoot");
            _registry = new RemoteEntityRegistry(_root.transform);

            _owner = new GameObject("Owner");
            _owner.transform.position = Vector3.zero;
            _owner.transform.rotation = Quaternion.identity;
            _input = _owner.AddComponent<InputManager>();

            _bound = null;
            _bindCalls = 0;
            _clearCalls = 0;

            _selector = _owner.AddComponent<NetworkTargetSelector>();
            _selector.Initialize(_registry, _owner.transform, _input, null,
                target =>
                {
                    _bound = target;
                    _bindCalls++;
                },
                () =>
                {
                    _bound = null;
                    _clearCalls++;
                },
                () => LocalId);
        }

        [TearDown]
        public void TearDown()
        {
            if (_owner != null) Object.DestroyImmediate(_owner);
            _registry.Clear();
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private RemoteEntityView CreateView(long entityId, Vector3 position,
            EntityKind kind = EntityKind.Player, bool dead = false)
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = entityId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                X = position.x, Y = position.y, Z = position.z,
                Kind = (byte)kind
            }, serverTicks: ServerTicks);

            var view = _registry.GetView(entityId);
            Assert.NotNull(view, $"视图未创建：{entityId}");
            view!.Position = position;   // ApplySnapshot 只入插值缓冲，测试直接写渲染位姿
            view.Dead = dead;
            return view;
        }

        private void PressSwitchAndTick()
        {
            _input.SetTargetSwitchPressedFromUI(true);
            _selector.Tick();
        }

        // ------------------------------------------------------------------
        // 纯函数筛选
        // ------------------------------------------------------------------

        [Test]
        public void Filter_Excludes_Dead_Bots_Out_Of_Range_And_Behind()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(1L, new Vector3(0, 0, 2f), (byte)EntityKind.Player, dead: false),
                new NetworkTargetSelector.Candidate(2L, new Vector3(0, 0, 3f), (byte)EntityKind.PlayerBot, dead: false),
                new NetworkTargetSelector.Candidate(3L, new Vector3(0, 0, 4f), (byte)EntityKind.Npc, dead: true),
                new NetworkTargetSelector.Candidate(4L, new Vector3(0, 0, 30f), (byte)EntityKind.Npc, dead: false),
                new NetworkTargetSelector.Candidate(5L, new Vector3(0, 0, -2f), (byte)EntityKind.Player, dead: false),
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 15f, halfAngleDeg: 90f);

            Assert.AreEqual(new[] { 1L }, result.ConvertAll(c => c.EntityId).ToArray(),
                "只有存活、在射程内且位于前向锥内的 Player 候选可被选中");
        }

        [Test]
        public void Filter_Includes_Living_Player_And_Npc()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(1L, new Vector3(0, 0, 2f), (byte)EntityKind.Player, dead: false),
                new NetworkTargetSelector.Candidate(2L, new Vector3(0, 0, 3f), (byte)EntityKind.Npc, dead: false),
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 15f, halfAngleDeg: 90f);

            Assert.AreEqual(new[] { 1L, 2L }, result.ConvertAll(c => c.EntityId).ToArray(),
                "Player 与 Npc 都可作为目标");
        }

        [Test]
        public void Filter_Excludes_Unknown_Kind()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(9L, new Vector3(0, 0, 2f), kind: 99, dead: false),
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 15f, halfAngleDeg: 90f);

            Assert.AreEqual(0, result.Count, "未知 EntityKind 必须 fail-closed（不参与锁定）");
        }

        [Test]
        public void Filter_Orders_By_Planar_Distance()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(1L, new Vector3(0, 0, 6f), (byte)EntityKind.Player, dead: false),
                new NetworkTargetSelector.Candidate(2L, new Vector3(0, 0, 2f), (byte)EntityKind.Npc, dead: false),
                new NetworkTargetSelector.Candidate(3L, new Vector3(1, 0, 1f), (byte)EntityKind.Player, dead: false),
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 15f, halfAngleDeg: 90f);

            Assert.AreEqual(new[] { 3L, 2L, 1L }, result.ConvertAll(c => c.EntityId).ToArray(),
                "候选按到角色的水平距离升序");
        }

        [Test]
        public void Filter_Includes_Large_Npc_By_Surface_Range_And_Angular_Reach()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(2L, new Vector3(2.2f, 0f, 0f),
                    (byte)EntityKind.Npc, dead: false, radius: 1.74f),
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 2f, halfAngleDeg: 60f);

            Assert.AreEqual(new[] { 2L }, result.ConvertAll(c => c.EntityId).ToArray(),
                "表面距 0.46 ≤ 2；中心角 90° ≤ 60° + asin(1.74/2.2) ≈ 112°");
        }

        [Test]
        public void Filter_Excludes_Small_Target_At_Same_Side_Angle()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(2L, new Vector3(1.5f, 0f, 0f),
                    (byte)EntityKind.Player, dead: false), // 半径 0
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 2f, halfAngleDeg: 60f);

            Assert.AreEqual(0, result.Count, "半径 0 时中心角 90° > 60°，必须排除");
        }

        [Test]
        public void Filter_Includes_Zero_Distance_Candidate()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(2L, Vector3.zero,
                    (byte)EntityKind.Npc, dead: false, radius: 1.74f),
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 2f, halfAngleDeg: 60f);

            Assert.AreEqual(new[] { 2L }, result.ConvertAll(c => c.EntityId).ToArray(),
                "零距离退化：角度无意义，直接入选");
        }

        [Test]
        public void Filter_Orders_By_Surface_Distance()
        {
            var candidates = new List<NetworkTargetSelector.Candidate>
            {
                new NetworkTargetSelector.Candidate(1L, new Vector3(0f, 0f, 3f),
                    (byte)EntityKind.Npc, dead: false, radius: 1.5f),   // 表面 1.5
                new NetworkTargetSelector.Candidate(2L, new Vector3(0f, 0f, 2f),
                    (byte)EntityKind.Player, dead: false),              // 表面 2.0
            };

            var result = NetworkTargetSelector.FilterCandidates(
                Vector3.zero, Vector3.forward, candidates, acquireRange: 15f, halfAngleDeg: 90f);

            Assert.AreEqual(new[] { 1L, 2L }, result.ConvertAll(c => c.EntityId).ToArray(),
                "按表面距离升序，而不是中心距");
        }

        // ------------------------------------------------------------------
        // T/按钮切换与每帧有效性
        // ------------------------------------------------------------------

        [Test]
        public void Update_Cycle_Binds_Nearest_Then_Next_And_Wraps()
        {
            CreateView(2L, new Vector3(0, 0, 3f));
            CreateView(3L, new Vector3(0, 0, 5f));

            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId, "首次切换选最近目标");
            Assert.IsNotNull(_bound);
            Assert.AreEqual(2L, _bound!.EntityId);

            PressSwitchAndTick();
            Assert.AreEqual(3L, _selector.LockedTargetId, "再次切换选下一个候选");

            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId, "候选列表循环回绕");
            Assert.AreEqual(3, _bindCalls);
            Assert.AreEqual(0, _clearCalls);
        }

        [Test]
        public void Update_Clears_Lock_When_Target_Dies()
        {
            var view = CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            view.Dead = true;
            _selector.Tick();

            Assert.AreEqual(0L, _selector.LockedTargetId, "目标死亡必须清锁");
            Assert.IsNull(_bound, "目标面板必须解绑");
            Assert.AreEqual(1, _clearCalls);
        }

        [Test]
        public void Update_Keeps_Lock_Within_Drop_Range_And_Clears_When_Leaving()
        {
            var view = CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            view.Position = new Vector3(0, 0, NetworkTargetSelector.DropRange - 1f);
            _selector.Tick();
            Assert.AreEqual(2L, _selector.LockedTargetId, "在保持半径内不得抖掉锁定");

            view.Position = new Vector3(0, 0, NetworkTargetSelector.DropRange + 1f);
            _selector.Tick();
            Assert.AreEqual(0L, _selector.LockedTargetId, "超出保持半径必须清锁");
            Assert.AreEqual(1, _clearCalls);
        }

        [Test]
        public void Update_Clears_Lock_When_Target_Leaves_Aoi()
        {
            CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            _registry.Remove(2L);   // AOI 出界/离线：视图移除
            _selector.Tick();

            Assert.AreEqual(0L, _selector.LockedTargetId, "目标出 AOI 必须清锁");
            Assert.AreEqual(1, _clearCalls);
        }

        [Test]
        public void SetOwnerAlive_False_Clears_Lock_And_Blocks_Cycle_Until_Revived()
        {
            CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            _selector.SetOwnerAlive(false);
            Assert.AreEqual(0L, _selector.LockedTargetId, "本地玩家死亡立即清锁");
            Assert.AreEqual(1, _clearCalls);

            PressSwitchAndTick();
            Assert.AreEqual(0L, _selector.LockedTargetId, "死亡期间切换输入必须被禁用");

            _selector.EnsureTargetFor(
                UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath));
            Assert.AreEqual(0L, _selector.LockedTargetId, "死亡期间施法不得重新锁定目标");

            _selector.SetOwnerAlive(true);
            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId, "复活后恢复切换");
        }

        [Test]
        public void RefreshTargetPanel_Rebinds_The_Locked_Target()
        {
            CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndTick();
            Assert.AreEqual(1, _bindCalls);

            _registry.UpdatePlayerName(2L, "Alice");   // 名册晚到
            _selector.RefreshTargetPanel();

            Assert.AreEqual(2, _bindCalls, "名册晚到应重绑目标面板");
            Assert.AreEqual("Alice", _bound!.DisplayName, "重绑后面板拿到更新后的名字");
        }

        [Test]
        public void Null_Optional_Bindings_Do_Not_Throw()
        {
            // 无 UIManager/SelectionRing/InputManager 的测试宿主也必须安全（?. 守卫）
            _selector.Initialize(_registry, _owner.transform, null, null, null, null, null);
            CreateView(2L, new Vector3(0, 0, 1.5f));

            Assert.DoesNotThrow(() => _selector.Tick());
            Assert.DoesNotThrow(() => _selector.EnsureTargetFor(
                UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath)));
            Assert.AreEqual(2L, _selector.LockedTargetId);
            Assert.DoesNotThrow(() => _selector.ClearLock());
        }

        // ------------------------------------------------------------------
        // 施法前取目标
        // ------------------------------------------------------------------

        [Test]
        public void EnsureTargetFor_Binds_Nearest_Within_Skill_Shape()
        {
            CreateView(2L, new Vector3(0, 0, 1.5f));
            CreateView(3L, new Vector3(0, 0, 5f));   // BA_Combo1 射程 2m：锥内但超射程
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath);
            Assert.NotNull(data, ComboSkillPath + " 未找到");

            _selector.EnsureTargetFor(data);

            Assert.AreEqual(2L, _selector.LockedTargetId,
                "无锁定时按技能形状选最近的合法目标，而不是全部候选里最近的");
            Assert.IsNotNull(_bound);
        }

        [Test]
        public void EnsureTargetFor_Keeps_An_Existing_Lock_Within_Skill_Shape()
        {
            CreateView(2L, new Vector3(0, 0, 1.5f));   // BA_Combo1 射程 2m、半角 60°：在形状内
            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            CreateView(3L, new Vector3(0, 0, 0.5f));   // 更近的候选晚到，但已有锁定不应被抢走
            _selector.EnsureTargetFor(
                UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath));

            Assert.AreEqual(2L, _selector.LockedTargetId, "技能形状内的有效锁定在施法时保留");
        }

        [Test]
        public void EnsureTargetFor_Clears_Lock_Outside_Skill_Range()
        {
            CreateView(2L, new Vector3(0, 0, 3f));     // 锁定合法（AcquireRange 15m），但 BA_Combo1 射程 2m
            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            _selector.EnsureTargetFor(
                UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath));

            Assert.AreEqual(0L, _selector.LockedTargetId,
                "施法瞬间锁定目标出技能射程：清锁回退 0（不得带显式目标去必空）");
            Assert.AreEqual(1, _clearCalls);
            Assert.IsNull(_bound);
        }

        [Test]
        public void EnsureTargetFor_Clears_Lock_Outside_Skill_Cone()
        {
            // 约 70° 偏角：T 的 ±90° 可以锁定，但 BA_Combo1 的半角是 60°
            float radians = 70f * Mathf.Deg2Rad;
            CreateView(2L, new Vector3(Mathf.Sin(radians) * 1.5f, 0f, Mathf.Cos(radians) * 1.5f));
            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            _selector.EnsureTargetFor(
                UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath));

            Assert.AreEqual(0L, _selector.LockedTargetId, "施法瞬间锁定目标出技能锥：清锁回退 0");
            Assert.AreEqual(1, _clearCalls);
        }

        [Test]
        public void EnsureTargetFor_Aoe_Uses_AreaRadius_Not_Range()
        {
            var spin = UnityEditor.AssetDatabase.LoadAssetAtPath<SpinSkillData>(
                "Assets/PreRes/SkillsCfg/Spin_SkillR.asset");
            Assert.NotNull(spin, "Spin_SkillR.asset 未找到");
            // Shape: AOE_Circle, Range=2, AreaRadius=2.5 ⇒ 2.2m 应保留（按 AreaRadius，而不是 Range）
            var view = CreateView(2L, new Vector3(0, 0, 2.2f));
            PressSwitchAndTick();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            _selector.EnsureTargetFor(spin);
            Assert.AreEqual(2L, _selector.LockedTargetId,
                "AOE 圆按 AreaRadius 复核（2.2m ≤ 2.5m），不按 Range=2m");

            view.Position = new Vector3(0, 0, 3f);   // 超出 AreaRadius 2.5m
            _selector.EnsureTargetFor(spin);
            Assert.AreEqual(0L, _selector.LockedTargetId, "AOE 半径外必须清锁回退 0");
            Assert.AreEqual(1, _clearCalls);
        }

        [Test]
        public void EnsureTargetFor_Without_Any_Target_In_Skill_Shape_Leaves_No_Lock()
        {
            CreateView(2L, new Vector3(0, 0, 5f));   // AcquireRange 内，但 BA_Combo1 射程外

            _selector.EnsureTargetFor(
                UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath));

            Assert.AreEqual(0L, _selector.LockedTargetId,
                "技能形状内无候选时回退 0（服务端自动目标），不得锁定射程外目标");
        }

        [Test]
        public void EnsureTargetFor_Keeps_Lock_On_Large_Npc_At_Melee_Range()
        {
            var spiderPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SpiderPrefab);
            Assert.NotNull(spiderPrefab, SpiderPrefab + " 未找到");

            var root = new GameObject("SpiderSelectorTestRoot");
            var registry = new RemoteEntityRegistry(root.transform, null,
                new Dictionary<int, GameObject> { { 4, spiderPrefab } });
            var owner = new GameObject("SpiderSelectorOwner");
            NetworkTargetable? bound = null;
            var selector = owner.AddComponent<NetworkTargetSelector>();
            selector.Initialize(registry, owner.transform, null, null,
                target => { bound = target; },
                () => { bound = null; },
                () => LocalId);
            try
            {
                const long npcId = 9_000_001L;
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = npcId,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind | EntityFieldMask.TemplateId),
                    X = 2.2f, Y = 0f, Z = 0f,
                    Kind = (byte)EntityKind.Npc, TemplateId = 4, Tick = 1
                }, serverTicks: ServerTicks);

                var view = registry.GetView(npcId);
                Assert.NotNull(view);
                view!.Position = new Vector3(2.2f, 0f, 0f);
                Assert.NotNull(view.BodyCollider, "真模型视图应补上运行时身体碰撞体");
                view.BodyCollider!.BuildCollider();   // EditMode：AddComponent 不触发生命周期
                Assert.Greater(view.BodyRadius, 1.5f, "蜘蛛的目标选择半径应接近 1.74");

                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath);
                Assert.NotNull(data, ComboSkillPath + " 未找到");
                selector.EnsureTargetFor(data);

                Assert.AreEqual(npcId, selector.LockedTargetId,
                    "侧面 90°、表面距 0.46m 的大体型目标应建立锁定");
                Assert.IsNotNull(bound);
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnsureTargetFor_Does_Not_Lock_Small_Target_At_Side()
        {
            CreateView(2L, new Vector3(1.5f, 0f, 0f));   // 玩家视图半径 0
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath);
            Assert.NotNull(data, ComboSkillPath + " 未找到");

            _selector.EnsureTargetFor(data);

            Assert.AreEqual(0L, _selector.LockedTargetId,
                "半径 0、中心角 90° > 60° 半锥：不得锁定");
        }
    }
}
