using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 渲染位移反推速度（design D17）。
    ///
    /// 为什么不取协议里的 `Speed`：动画要匹配的是**屏幕上实际的移动**，而渲染位移的导数
    /// 就是这个量；服务端上报的是它的意图（怪物的 `Moving` 标志换算），可能与渲染有偏差。
    /// 顺带免掉穿协议、改 `AddSample` 签名、改插值器测试。
    ///
    /// 两条工程约束：
    ///   · **钳位**：重生/服务器校正会让位置瞬移，导数爆掉 → 钳到 `maxSpeed`（调用方传 1.5×SprintSpeed）；
    ///   · **平滑**：快照 15Hz ⇒ 位移导数呈台阶，指数平滑（时间常数 0.15s）磨平；平滑只延迟，不改终值。
    /// </summary>
    public static class RenderSpeedEstimator
    {
        /// <summary>服务器时钟换算：1ms = 10_000 tick（与 `ClientNetworkManager` 换算 renderDelay 同基准）</summary>
        public const float TicksPerSecond = 10_000_000f;

        /// <summary>平滑时间常数（秒）</summary>
        public const float SmoothingTau = 0.15f;

        /// <summary>
        /// 推进一步。`deltaSeconds &lt;= 0`（首帧 / 时钟未推进）时**保持**上一值。
        /// </summary>
        public static float Step(float previous, Vector3 from, Vector3 to, float deltaSeconds, float maxSpeed)
        {
            if (deltaSeconds <= 0f) return previous;

            float raw = (to - from).magnitude / deltaSeconds;
            if (raw > maxSpeed) raw = maxSpeed;

            float alpha = 1f - Mathf.Exp(-deltaSeconds / SmoothingTau);
            return previous + (raw - previous) * alpha;
        }
    }
}
