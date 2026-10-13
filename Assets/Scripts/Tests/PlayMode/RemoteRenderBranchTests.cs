using System.Collections;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 远端实体按 `EntityKind` 分支机构（N5b → 2026-10-13 统一）。
    ///
    /// **反转记录**：这里原先钉的是"bot 必须是胶囊占位"（D-P5-7），理由是"100 个蒙皮网格会
    /// 压垮手机浏览器"。该前提已被实测推翻——AOI（80m 视野）之后客户端的同屏实体数由
    /// **局部密度**决定，`--bots 100` 撒在 ±400m 时的实测峰值只有 5~6 个
    /// （production/verify/t12、t13 的 AoiWorldTests 输出），客户端从来没有同时渲染过 100 个。
    /// 所以 bot 与真人合并到同一条真模型路径（design D1/D6），数量由分档骨架的**每帧预算**兜底。
    ///
    /// 用例自带一个"角色预制体"（运行时 GameObject + Animator）而不是去磁盘上取真预制体：
    /// PlayMode 测试程序集在非 Editor 平台也要能跑，不能引用 `UnityEditor.AssetDatabase`。
    /// 这里要钉的是**路径契约**——两种 Kind 拿到的是同一种东西，而不是某个具体预制体的身份。
    /// </summary>
    public class RemoteRenderBranchTests
    {
        /// <summary>角色预制体上的"本地行为"替身——用来钉住"远程角色只许跑动画驱动"这条。</summary>
        private sealed class DecoyBehaviour : MonoBehaviour { }

        private GameObject _root = null!;
        private GameObject _avatarPrefab = null!;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("RemoteRenderRoot");

            _avatarPrefab = new GameObject("AvatarPrefabDouble");
            _avatarPrefab.AddComponent<Animator>();
            _avatarPrefab.AddComponent<BoxCollider>();
            _avatarPrefab.AddComponent<DecoyBehaviour>();

            _registry = new RemoteEntityRegistry(_root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = _avatarPrefab } });
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Clear();
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_avatarPrefab);
        }

        private static EntitySnapshot Snap(long playerId, byte kind)
        {
            return new EntitySnapshot
            {
                PlayerId = playerId,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                    | EntityFieldMask.Kind | EntityFieldMask.ActionState | EntityFieldMask.Dead),
                X = 1f, Y = 0f, Z = 2f,
                Rotation = 0f, Kind = kind, Tick = 1
            };
        }

        [UnityTest]
        public IEnumerator Real_Player_And_Bot_Take_The_Same_Model_Path()
        {
            // 两个快照走同一条 ApplySnapshot：只有 Kind 不同
            var real = Snap(playerId: 1, kind: (byte)EntityKind.Player);
            var bot = Snap(playerId: 2, kind: (byte)EntityKind.PlayerBot);

            _registry.ApplySnapshot(real, 0);
            _registry.ApplySnapshot(bot, 0);
            yield return null;

            var realView = _registry.GetView(1)!;
            var botView = _registry.GetView(2)!;

            Assert.IsNotNull(realView.GameObject.GetComponentInChildren<Animator>(true),
                "真人必须拿到带 Animator 的角色");
            Assert.IsNotNull(botView.GameObject.GetComponentInChildren<Animator>(true),
                "bot 走的是同一条真模型路径（D1/D6——旧断言\"D-P5-7: bot 必须是占位\"已被实测推翻）");
            Assert.IsNotNull(botView.Animator, "bot 也必须挂上行走动画驱动");
            Assert.IsNotNull(realView.Animator, "真人挂驱动");
            Assert.IsNull(botView.GameObject.GetComponentInChildren<NpcBodyCollider>(true),
                "bot 不挡路（D5：远端实体是视图不是物理体）");
        }

        /// <summary>
        /// 远程角色是"别人权威状态的视图"：预置在角色预制体上的**本地行为**一条都不许跑。
        /// 最容易看到后果的一条是 `Sys3CEntry.Start` —— 它会 `FindObjectOfType&lt;ThirdPersonCameraController&gt;()`
        /// 再把相机 Target 抢到远程角色身上（本地镜头跟着别人跑）。
        /// 这里用替身组件 + 碰撞体钉住这条规则：**只有动画驱动活着**。
        /// </summary>
        [UnityTest]
        public IEnumerator Remote_Avatar_Runs_Only_Its_Animator_Driver()
        {
            _registry.ApplySnapshot(Snap(playerId: 5, kind: (byte)EntityKind.Player), 0);
            yield return null;

            var avatar = _registry.GetView(5)!.GameObject;

            Assert.IsNotNull(avatar.GetComponentInChildren<RemoteEntityAnimator>(true),
                "动画驱动本身必须还在跑");
            Assert.IsFalse(avatar.GetComponentInChildren<DecoyBehaviour>(true)!.enabled,
                "角色预制体上的本地行为必须被停掉（相机被抢走的来源就在这里）");
            Assert.IsFalse(avatar.GetComponentInChildren<BoxCollider>(true)!.enabled,
                "碰撞体也要停——远程位置由快照插值写，物理不该插一脚");
            Assert.IsNotNull(avatar.GetComponentInChildren<Animator>(true),
                "Animator 不是 MonoBehaviour，不该被一起停掉");
        }

        /// <summary>
        /// `Dead` 是**状态**不是边沿事件（D-N1/D-N2）：观察者已在视野内时，最多要等 4 tick
        /// （≈264ms）静止保活窗口才看到它翻位。所以驱动必须**每次快照都读状态**——
        /// 写成"看到翻位才切动画"会漏掉那 264ms 窗口，远程角色（服务端 `ActionState` 玩家侧
        /// 恒 0，D-P5-5）就永远演不出死亡。
        ///
        /// 判据取在**映射结果**上（`LastHitState`）而不是 Animator 的实际参数：测试里的空
        /// Animator 没有控制器，驱动对它不写（真预制体带 `Character3C.controller`，走的是同一条代码路径）。
        /// </summary>
        [UnityTest]
        public IEnumerator Snapshot_Dead_State_Maps_To_The_Death_HitState()
        {
            _registry.ApplySnapshot(Snap(playerId: 3, kind: (byte)EntityKind.Player), 0);
            yield return null;

            // 同一条视图（已经存在，不再走 CreateView）——死亡是后续快照里翻位的一位
            var dead = Snap(playerId: 3, kind: (byte)EntityKind.Player);
            dead.Dead = true;
            _registry.ApplySnapshot(dead, 1_000_000L);
            yield return null;

            var view = _registry.GetView(3);
            Assert.IsNotNull(view, "视图应仍在（死亡不等于离场）");
            Assert.IsTrue(view!.Dead, "视图要记下权威存活状态");
            Assert.IsNotNull(view.Animator, "真人视图必须挂上动画驱动");
            Assert.AreEqual((int)HitState.Death, view.Animator!.LastHitState,
                "快照的 Dead 位必须映射到控制器的死亡状态（不是起身——两套枚举同值不同名）");
        }

        /// <summary>未接线角色预制体时退化为占位——绝不 `Instantiate(null)`（测试宿主与未配置场景是这条）。</summary>
        [UnityTest]
        public IEnumerator Unwired_Avatar_Prefab_Falls_Back_To_A_Placeholder()
        {
            var fallbackRoot = new GameObject("FallbackRoot");
            var registry = new RemoteEntityRegistry(fallbackRoot.transform);
            try
            {
                registry.ApplySnapshot(Snap(playerId: 4, kind: (byte)EntityKind.Player), 0);
                yield return null;

                var view = registry.GetView(4);
                Assert.IsNotNull(view, "没有角色预制体也要有视图（否则 Scene 未接线时远程玩家整个消失）");
                Assert.IsTrue(view!.GameObject.name.Contains("RemotePlayer"),
                    $"占位名仍须标记玩家：'{view.GameObject.name}'");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(fallbackRoot);
            }
        }
    }
}
