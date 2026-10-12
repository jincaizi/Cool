using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 怪物视图的运行时身体碰撞体（纯客户端，2026-10-13）——让**本地玩家**撞不过去（Boss 不再能穿过）。
    ///
    /// 为什么加在 MonoBehaviour 上：项目规则禁止在非 MonoBehaviour 上下文 GetComponent；registry 是
    /// 普通类，只能 AddComponent（与 NpcEntityAnimator 同款），由本组件自己取渲染器。
    ///
    /// 三条设计约束（详见 docs/superpowers/plans/2026-10-13-spawn-and-collision-report.md）：
    ///  1. **尺寸从模型推**：合并视图里 SkinnedMeshRenderer 的 bounds（世界空间）→ 换算回视图本地空间，
    ///     竖直棱柱（BoxCollider）近似圆柱：XZ 跨度 = 方形包络、Y 跨度 = 高度。DogKnight 与 SpiderBoss
    ///     共用一个公式（缩放差异由 lossyScale 除法吸收）。<b>为什么不是胶囊</b>：胶囊的穹顶会被
    ///     CharacterController 顺坡爬上去（实测站到 Boss 头顶），棱柱侧面竖直、爬不上去。
    ///  2. **只与本地玩家交互**：碰撞体在独立层 <see cref="BodyLayerName"/>，层矩阵里只有玩家层
    ///     <see cref="LocalPlayerLayer"/> 与它碰撞（其余层全关，见 NpcBodyColliderTests 的守护用例）。
    ///     没有 Rigidbody —— 静态碰撞体：挡得住 CharacterController，自己不会被物理推走。
    ///  3. **不污染其它物理查询**：攻击/AOE 检测按组件筛选（IEffectTarget / IDamageable），本视图没有
    ///     这些组件（有守护用例）；冲刺障碍检测掩码只含 Default；地面检测读 CharacterController.isGrounded。
    ///
    /// 已知代价：这是**纯客户端**判定——服务端不模拟玩家移动、也不校验重叠 ⇒ **改客户端就能穿模**。
    /// </summary>
    public sealed class NpcBodyCollider : MonoBehaviour
    {
        /// <summary>本地玩家所在层（玩家 prefab 根 + CharacterController 实测值；换了层守护用例会红）。</summary>
        internal const int LocalPlayerLayer = 9;

        /// <summary>怪物身体碰撞体所在层：TagManager 第 10 层，层名 <see cref="BodyLayerName"/>。
        /// 用**索引**而不是 <c>LayerMask.NameToLayer</c>：层名的运行时缓存会因编辑器域重载变陈旧
        /// （磁盘与构建里的名字是对的——测试直接断言 TagManager 资产守住 name↔index 配对）；
        /// 层索引是编译期常量，编辑器与构建里都稳。层矩阵只放行 <see cref="LocalPlayerLayer"/>。</summary>
        public const int BodyLayer = 10;

        /// <summary>该层的可读名（TagManager 注册；人类读 Layer 下拉框/诊断用，代码不依赖它）。</summary>
        public const string BodyLayerName = "NpcBody";

        /// <summary>建出来的身体碰撞体（未建成 / 模型无渲染器时为 null）</summary>
        public BoxCollider? Collider { get; private set; }

        private void Awake() => BuildCollider();

        /// <summary>
        /// 建碰撞体。独立成 <c>internal</c> 方法而不是只塞进 <c>Awake</c>：EditMode 下 <c>AddComponent</c>
        /// 不触发生命周期（与 NpcEntityAnimator.ResolveAnimator 同惯例），测试要直接调它。
        /// </summary>
        internal void BuildCollider()
        {
            if (Collider != null) return;   // 幂等：Awake 与测试显式调用可能都来一次

            var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned.Length == 0)
            {
                Debug.LogWarning("[Sync] monster view has no SkinnedMeshRenderer; " +
                                 "no body collider built (vertices will pass through)");
                return;
            }

            // 合并各 SMR 的世界空间 bounds（骨骼姿态下 Renderer.bounds 即当前蒙皮包围盒）
            var world = skinned[0].bounds;
            for (int i = 1; i < skinned.Length; i++) world.Encapsulate(skinned[i].bounds);

            // 世界 → 视图本地：中心走 InverseTransformPoint；尺寸除回 lossyScale（Boss 根上挂着 2× 缩放）
            var scale = transform.lossyScale;
            var localSize = new Vector3(
                world.size.x / Mathf.Max(Mathf.Abs(scale.x), 1e-4f),
                world.size.y / Mathf.Max(Mathf.Abs(scale.y), 1e-4f),
                world.size.z / Mathf.Max(Mathf.Abs(scale.z), 1e-4f));
            if (localSize.x < 0.01f && localSize.y < 0.01f && localSize.z < 0.01f) return; // 未蒙皮的退化 bounds

            // **竖直棱柱（BoxCollider）而不是胶囊**：圆柱/胶囊的顶是**穹顶**，CharacterController 的
            // 滑动解算会顺着穹顶爬上去（2026-10-13 实测：玩家真的站到了 Boss 头顶，中心距 0）——
            // 那不是"挡住"。棱柱四条侧棱面是竖直面（90° > slopeLimit），CC 爬不上，顶是平的；
            // 尺寸仍从模型 bounds 推（XZ 跨度 = 圆柱直径的方形包络，Y 跨度 = 高），不手填魔数。
            // 碰撞体挂在子物体上：**不动美术层**（渲染剔除按 GameObject 所在层走），
            // 也方便验收/诊断（Hierarchy 里认 `NpcBody` 这个名字）。
            var body = new GameObject("NpcBody");
            body.layer = BodyLayer;
            body.transform.SetParent(transform, false);
            body.transform.localPosition = transform.InverseTransformPoint(world.center);

            var box = body.AddComponent<BoxCollider>();
            box.size = localSize;
            box.isTrigger = false;  // 实心：CharacterController 才会被挡
            Collider = box;
        }
    }
}
