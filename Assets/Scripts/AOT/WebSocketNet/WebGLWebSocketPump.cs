#if UNITY_WEBGL
using UnityEngine;

namespace WebSocketNet
{
    /// <summary>
    /// WebGLWebSocketChannel 的帧泵。
    ///
    /// 浏览器的 WebSocket 回调只改 JS 侧状态，帧要由 C# 主动来取；那个"取"
    /// 必须发生在 Unity 主线程上，所以这里挂一个隐藏的 Update。
    ///
    /// 单独成文件而不是内嵌类：Unity 对 MonoBehaviour 的约定是"一个文件一个
    /// 同名类型"，两者不一致时 AddComponent 的行为不保证。
    /// </summary>
    internal sealed class WebGLWebSocketPump : MonoBehaviour
    {
        internal WebGLWebSocketChannel? Target;

        private void Update()
        {
            Target?.Pump();
        }
    }
}
#endif
