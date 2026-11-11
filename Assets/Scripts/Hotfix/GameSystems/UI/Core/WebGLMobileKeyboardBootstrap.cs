#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// Unity WebGL 的移动软键盘开关默认关闭：不打开它，手机浏览器里
    /// `TouchScreenKeyboard.isSupported` 为 false，TMP_InputField 走桌面键盘分支，
    /// 点击输入框不会弹出系统输入法。这里在首个场景加载前打开，覆盖选人面板的
    /// 昵称 / 服务器地址 / 端口输入框。
    ///
    /// 仅 WebGL 构建可编译（`WebGLInput` 在 WebGL 模块里），编辑器/PC 无此问题。
    /// 无法用 EditMode 单测覆盖：该开关只在 WebGL 运行时生效，验证靠真机。
    /// </summary>
    internal static class WebGLMobileKeyboardBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Enable()
        {
            WebGLInput.mobileKeyboardSupport = true;
        }
    }
}
#endif
