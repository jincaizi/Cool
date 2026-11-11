using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// TMP 3.0.6 在 WebGL 上把 `shouldHideSoftKeyboard` 判成 true（平台 switch 的 default 分支），
    /// `InPlaceEditing()` 因此走桌面键盘分支，永不调用 `TouchScreenKeyboard.Open`。
    /// 手机浏览器没有硬件键盘，点击 TMP_InputField 不会出现任何输入法。
    ///
    /// 本组件在点击时主动打开 Unity WebGL 的移动键盘（页面底部 HTML 输入条），并把文字回写到
    /// TMP 输入框。仅在 WebGL 构建里生效；PC/编辑器里是空组件（不注册任何输入）。
    /// 自动取同 GameObject 上的 TMP_InputField，便于直接挂在 prefab 的输入框节点上。
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public sealed class TmpInputMobileKeyboardBridge : MonoBehaviour, IPointerClickHandler
    {
        private TMP_InputField _field;
#if UNITY_WEBGL && !UNITY_EDITOR
        private TouchScreenKeyboard _keyboard;
#endif

        private void Awake() => _field = GetComponent<TMP_InputField>();

        public void OnPointerClick(PointerEventData eventData)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_field == null || _keyboard != null) return;

            _keyboard = TouchScreenKeyboard.Open(
                _field.text,
                _field.keyboardType,
                false,
                _field.lineType != TMP_InputField.LineType.SingleLine,
                _field.contentType == TMP_InputField.ContentType.Password,
                false,
                string.Empty,
                _field.characterLimit);
#endif
        }

        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_keyboard == null) return;

            if (_keyboard.active)
            {
                if (_field.text != _keyboard.text)
                {
                    _field.text = _keyboard.text;
                    _field.caretPosition = _field.text.Length;
                }
                return;
            }

            // 关闭（点 OK / 浏览器收回键盘）时补一次最终值。
            if (_field.text != _keyboard.text)
                _field.text = _keyboard.text;
            _keyboard = null;
#endif
        }

        private void OnDisable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_keyboard == null) return;
            _keyboard.active = false;
            _keyboard = null;
#endif
        }
    }
}
