using System.Collections.Generic;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 2026-10-13：MobileInputBinder 的"挑本地 entry"判据。
    ///
    /// 缺陷形态：重发现窗口（玩家被 PlayerSpawner 销毁重建、`_inputManager == null`）里
    /// `FindObjectOfType<Sys3CEntry>()` 会返回**远端头像**上的禁用组件
    /// （`RemoteEntityAnimator.Awake` 只把它 `enabled = false`，GameObject 仍激活 ⇒ 找得到）——
    /// 摇杆适配器装到别人身上，手机上摇杆与三个动作按钮**完全没反应且无日志**。
    ///
    /// 这里钉住判据：候选自身或父链上存在 `RemoteEntityAnimator`（远端驱动）即远端、被排除；
    /// 全远端 ⇒ null；混合 ⇒ 挑本地；纯本地 ⇒ 原样返回。
    /// </summary>
    public class MobileInputBinderLocalEntryTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("MobileInputBinderLocalEntryTestRoot");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        /// <summary>
        /// 造一个"远端视图"候选：视图根挂 `RemoteEntityAnimator`（真机上由
        /// `RemoteEntityRegistry` 的 `avatar.AddComponent&lt;RemoteEntityAnimator&gt;()` 挂上）、
        /// 其下挂着被禁用的 `Sys3CEntry`（EditMode 下 `AddComponent` 不触发生命周期，
        /// 所以 `RemoteEntityAnimator.Awake` 的禁用循环要在这里手工模拟——形态与真机一致）。
        /// </summary>
        private Sys3CEntry CreateRemoteCandidate(string name)
        {
            var viewRoot = new GameObject(name);
            viewRoot.transform.SetParent(_root.transform, false);
            viewRoot.AddComponent<RemoteEntityAnimator>();

            var avatar = new GameObject(name + "Avatar");
            avatar.transform.SetParent(viewRoot.transform, false);
            var entry = avatar.AddComponent<Sys3CEntry>();
            entry.enabled = false;   // = RemoteEntityAnimator.Awake 禁掉组件、但 GameObject 仍激活
            return entry;
        }

        private Sys3CEntry CreateLocalCandidate(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            return go.AddComponent<Sys3CEntry>();
        }

        [Test]
        public void test_pick_local_entry_excludes_remote_only_candidates()
        {
            // Arrange：候选全是远端视图（修复前 `FindObjectOfType` 正会选中它）
            var remote = CreateRemoteCandidate("RemoteView");

            // Act
            var picked = MobileInputBinder.PickLocalEntry(new List<Sys3CEntry> { remote });

            // Assert（本修复的核心：不许绑到远端 —— 否则摇杆适配器装到别人身上，手机静默失效）
            Assert.IsNull(picked, "全是远端时必须返回 null（不绑定），不能把适配器装到远端头像上");
        }

        [Test]
        public void test_pick_local_entry_prefers_local_when_mixed()
        {
            // Arrange：远端在前 —— 朴素"取第一个"会挑错
            var remote = CreateRemoteCandidate("RemoteView");
            var local = CreateLocalCandidate("LocalPlayer");

            // Act
            var picked = MobileInputBinder.PickLocalEntry(new List<Sys3CEntry> { remote, local });

            // Assert
            Assert.AreSame(local, picked, "混合候选必须跳过远端、选中本地");
        }

        [Test]
        public void test_pick_local_entry_returns_the_only_local_candidate()
        {
            // Arrange
            var local = CreateLocalCandidate("LocalPlayer");

            // Act
            var picked = MobileInputBinder.PickLocalEntry(new List<Sys3CEntry> { local });

            // Assert
            Assert.AreSame(local, picked, "纯本地候对应原样返回（不破坏既有行为）");
        }

        [Test]
        public void test_pick_local_entry_excludes_a_remote_driver_on_the_same_game_object()
        {
            // Arrange：真机布局 —— `avatar.AddComponent<RemoteEntityAnimator>()` 与预制体自带的
            // `Sys3CEntry` 在**同一个** GameObject 上（父链判据必须含候选自身）
            var avatar = new GameObject("RemoteAvatarSameGo");
            avatar.transform.SetParent(_root.transform, false);
            avatar.AddComponent<RemoteEntityAnimator>();
            var entry = avatar.AddComponent<Sys3CEntry>();
            entry.enabled = false;

            // Act
            var picked = MobileInputBinder.PickLocalEntry(new List<Sys3CEntry> { entry });

            // Assert
            Assert.IsNull(picked, "远端驱动与 entry 同物体时同样要排除（真机就是这种布局）");
        }
    }
}
