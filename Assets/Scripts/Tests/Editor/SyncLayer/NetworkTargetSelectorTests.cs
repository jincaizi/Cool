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

        private void PressSwitchAndUpdate()
        {
            _input.SetTargetSwitchPressedFromUI(true);
            _selector.Update();
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

        // ------------------------------------------------------------------
        // T/按钮切换与每帧有效性
        // ------------------------------------------------------------------

        [Test]
        public void Update_Cycle_Binds_Nearest_Then_Next_And_Wraps()
        {
            CreateView(2L, new Vector3(0, 0, 3f));
            CreateView(3L, new Vector3(0, 0, 5f));

            PressSwitchAndUpdate();
            Assert.AreEqual(2L, _selector.LockedTargetId, "首次切换选最近目标");
            Assert.IsNotNull(_bound);
            Assert.AreEqual(2L, _bound!.EntityId);

            PressSwitchAndUpdate();
            Assert.AreEqual(3L, _selector.LockedTargetId, "再次切换选下一个候选");

            PressSwitchAndUpdate();
            Assert.AreEqual(2L, _selector.LockedTargetId, "候选列表循环回绕");
            Assert.AreEqual(3, _bindCalls);
            Assert.AreEqual(0, _clearCalls);
        }

        [Test]
        public void Update_Clears_Lock_When_Target_Dies()
        {
            var view = CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndUpdate();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            view.Dead = true;
            _selector.Update();

            Assert.AreEqual(0L, _selector.LockedTargetId, "目标死亡必须清锁");
            Assert.IsNull(_bound, "目标面板必须解绑");
            Assert.AreEqual(1, _clearCalls);
        }

        [Test]
        public void Update_Keeps_Lock_Within_Drop_Range_And_Clears_When_Leaving()
        {
            var view = CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndUpdate();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            view.Position = new Vector3(0, 0, NetworkTargetSelector.DropRange - 1f);
            _selector.Update();
            Assert.AreEqual(2L, _selector.LockedTargetId, "在保持半径内不得抖掉锁定");

            view.Position = new Vector3(0, 0, NetworkTargetSelector.DropRange + 1f);
            _selector.Update();
            Assert.AreEqual(0L, _selector.LockedTargetId, "超出保持半径必须清锁");
            Assert.AreEqual(1, _clearCalls);
        }

        [Test]
        public void Update_Clears_Lock_When_Target_Leaves_Aoi()
        {
            CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndUpdate();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            _registry.Remove(2L);   // AOI 出界/离线：视图移除
            _selector.Update();

            Assert.AreEqual(0L, _selector.LockedTargetId, "目标出 AOI 必须清锁");
            Assert.AreEqual(1, _clearCalls);
        }

        [Test]
        public void SetOwnerAlive_False_Clears_Lock_And_Blocks_Cycle_Until_Revived()
        {
            CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndUpdate();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            _selector.SetOwnerAlive(false);
            Assert.AreEqual(0L, _selector.LockedTargetId, "本地玩家死亡立即清锁");
            Assert.AreEqual(1, _clearCalls);

            PressSwitchAndUpdate();
            Assert.AreEqual(0L, _selector.LockedTargetId, "死亡期间切换输入必须被禁用");

            _selector.EnsureTargetFor(
                UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath));
            Assert.AreEqual(0L, _selector.LockedTargetId, "死亡期间施法不得重新锁定目标");

            _selector.SetOwnerAlive(true);
            PressSwitchAndUpdate();
            Assert.AreEqual(2L, _selector.LockedTargetId, "复活后恢复切换");
        }

        [Test]
        public void RefreshTargetPanel_Rebinds_The_Locked_Target()
        {
            CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndUpdate();
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

            Assert.DoesNotThrow(() => _selector.Update());
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
        public void EnsureTargetFor_Keeps_An_Existing_Valid_Lock()
        {
            CreateView(2L, new Vector3(0, 0, 3f));
            PressSwitchAndUpdate();
            Assert.AreEqual(2L, _selector.LockedTargetId);

            CreateView(3L, new Vector3(0, 0, 0.5f));   // 更近的候选晚到，但已有锁定不应被抢走
            _selector.EnsureTargetFor(
                UnityEditor.AssetDatabase.LoadAssetAtPath<ComboSkillData>(ComboSkillPath));

            Assert.AreEqual(2L, _selector.LockedTargetId, "有效锁定在施法时保留");
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
    }
}
