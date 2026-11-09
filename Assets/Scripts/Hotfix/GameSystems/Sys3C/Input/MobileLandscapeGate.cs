using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 浏览器方向门 —— 轮询页面模板在 Unity 加载前写入的 `window.CoolLandscape`，
    /// 把"竖屏遮罩是否显示"同步到 <see cref="InputBlockGate"/>。
    ///
    /// 常驻 `DontDestroyOnLoad`：角色尚未创建、销毁重建、切换场景都不能让阻断失效。
    /// 页面状态未就绪时保持阻断（fail-closed）——只有在 MobileLandscape 模板缺失时才会
    /// 长期停在这个状态，日志会明确提示。
    /// </summary>
    public class MobileLandscapeGate : MonoBehaviour
    {
        private const float BridgeWarningSeconds = 2f;

        private float _startedAt;
        private bool _warnedMissingBridge;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern int WebGLLandscapeGate_GetState();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var go = new GameObject("[MobileLandscapeGate]");
            DontDestroyOnLoad(go);
            go.AddComponent<MobileLandscapeGate>();
#endif
        }

        private void Awake()
        {
            _startedAt = Time.realtimeSinceStartup;

            // 页面状态同步完成前，移动 WebGL 的游戏操作默认关闭。
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
        }

        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int state = WebGLLandscapeGate_GetState();
            if (state < 0)
            {
                if (!_warnedMissingBridge
                    && Time.realtimeSinceStartup - _startedAt >= BridgeWarningSeconds)
                {
                    _warnedMissingBridge = true;
                    UnityEngine.Debug.LogWarning("[MobileLandscape] 页面未提供方向状态，输入保持阻断；"
                        + "请使用 MobileLandscape WebGL 模板出包。");
                }
                return;
            }

            _warnedMissingBridge = false;
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, state == 1);
#endif
        }
    }
}
