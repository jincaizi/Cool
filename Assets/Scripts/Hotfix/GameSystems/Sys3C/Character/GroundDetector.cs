using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 地面检测器 — 使用 UnityEngine.CharacterController.isGrounded
    /// </summary>
    public class GroundDetector
    {
        /// <summary>接地探针的竖直容差（米）——只覆盖 skin/台阶误差，不放宽成空中门控。</summary>
        private const float ProbeSkin = 0.06f;

        /// <summary>NpcBody 层索引（与 SyncLayer.NpcBodyCollider 同值；Sys3C 不能反向依赖 SyncLayer）。</summary>
        private const int NpcBodyLayer = 10;
        /// <summary>本地玩家层索引（GroundLayer 未配置时的兜底掩码要排除自身与怪物身体）。</summary>
        private const int LocalPlayerLayer = 9;

        private readonly UnityEngine.CharacterController _controller;
        private readonly LayerMask _groundLayer;

        public GroundDetector(UnityEngine.CharacterController controller, LayerMask groundLayer)
        {
            _controller = controller;
            _groundLayer = groundLayer;
        }

        /// <summary>
        /// 检测是否在地面上。`CharacterController.isGrounded` 在贴住 NpcBody（层 10）侧面时
        /// 会闪烁 false（B5 实测：贴怪时 flags=Sides、grounded=false，跳跃请求被门控拒绝）。
        /// 这里用只含 GroundLayer 的脚下球体补一次接地证据：NpcBody 不在掩码里，侧面接触不会误判；
        /// 探针只覆盖贴地容差，真正空中（跳跃/坠落）仍返回 false。
        /// </summary>
        public bool IsGrounded()
        {
            if (_controller.isGrounded) return true;
            if (!_controller.enabled) return false;

            // 胶囊底部球心：position + center − up·(height/2 − radius)；
            // CharacterController 静止时与地面之间留有 skinWidth（实测 0.09m），探针必须
            // 向下覆盖这段皮肤间隙 + 小容差，否则贴地也会判 false。
            float radius = Mathf.Max(0.05f, _controller.radius * 0.9f);
            float skin = Mathf.Max(0.02f, _controller.skinWidth);
            Vector3 bottomSphere = _controller.transform.position + _controller.center
                                   - Vector3.up * (_controller.height * 0.5f - _controller.radius);
            Vector3 probeCenter = bottomSphere
                                  - Vector3.up * (_controller.radius - radius + skin + 0.02f);
            int mask = _groundLayer.value != 0
                ? _groundLayer.value
                : ~((1 << LocalPlayerLayer) | (1 << NpcBodyLayer));
            return Physics.CheckSphere(probeCenter, radius, mask, QueryTriggerInteraction.Ignore)
                || Physics.Raycast(probeCenter, Vector3.down, ProbeSkin, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 贴怪支撑证据（B5）：胶囊与 NpcBody（层 10）相交时允许起跳。
        /// 怪物身体是实心盒体，贴边/压入会让 isGrounded 丢失且脚下地面探针可能够不到；
        /// 这一支只在玩家确实贴着怪物时成立，不放松普通空中门控（层 10 只用于怪物身体）。
        /// </summary>
        public bool HasBodySupport()
        {
            if (!_controller.enabled) return false;
            Vector3 bottomSphere = _controller.transform.position + _controller.center
                                   - Vector3.up * (_controller.height * 0.5f - _controller.radius);
            Vector3 topSphere = bottomSphere
                                + Vector3.up * Mathf.Max(0f, _controller.height - 2f * _controller.radius);
            // CharacterController 与盒体之间会留下 skinWidth + 接触偏移的间隙（实测 0.14–0.20m），
            // 用精确胶囊会判成“未接触”；按 0.25m 容差膨胀，只识别真正贴边的怪（层 10 仅怪物身体）。
            float probeRadius = _controller.radius + 0.25f;
            return Physics.CheckCapsule(bottomSphere, topSphere, probeRadius,
                1 << NpcBodyLayer, QueryTriggerInteraction.Ignore);
        }
    }
}
