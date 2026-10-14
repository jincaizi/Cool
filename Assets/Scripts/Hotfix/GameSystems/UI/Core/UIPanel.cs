using DG.Tweening;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public abstract class UIPanel : MonoBehaviour
    {
        public abstract LayerType Layer { get; }
        public abstract VisibilityMode Mode { get; }
        public abstract string PanelId { get; }

        public Canvas Canvas { get; private set; }
        public CanvasGroup CanvasGroup { get; private set; }
        public bool IsVisible { get; private set; }

        protected virtual void Awake()
        {
            Canvas = GetComponent<Canvas>();
            CanvasGroup = GetComponent<CanvasGroup>();
        }

        internal virtual void OnPreShow() { }

        /// <summary>入场动画钩子。默认不播；阶段 5 在此接入实现。</summary>
        internal virtual Sequence PlayShowAnimation() => null;

        internal virtual void OnShow() { }
        internal virtual void OnHide() { }

        /// <summary>出场动画钩子。默认不播；阶段 5 在此接入实现。</summary>
        internal virtual Sequence PlayHideAnimation() => null;

        internal void SetVisible(bool visible)
        {
            IsVisible = visible;
        }

        internal void ApplyVisibilityOff()
        {
            switch (Mode)
            {
                case VisibilityMode.ToggleActive:
                    gameObject.SetActive(false);
                    break;
                case VisibilityMode.CanvasSwitch:
                    if (Canvas != null) Canvas.enabled = false;
                    break;
                case VisibilityMode.CanvasGroup:
                    if (CanvasGroup != null)
                    {
                        CanvasGroup.alpha = 0f;
                        CanvasGroup.blocksRaycasts = false;
                    }
                    break;
            }
        }

        /// <summary>
        /// 声明的显隐模式与预制体实际组件是否匹配。返回 null = 匹配，否则返回人话的原因。
        ///
        /// 为什么要有这个：`ApplyVisibilityOff/On` 每个分支都判空，于是**声明与预制体不符时
        /// 是静默失效** —— 选 CanvasSwitch 而预制体没有 Canvas，面板就永远关不掉，且零日志。
        /// </summary>
        internal static string DescribeModeMismatch(VisibilityMode mode, bool hasCanvas, bool hasCanvasGroup)
        {
            switch (mode)
            {
                case VisibilityMode.CanvasSwitch:
                    return hasCanvas
                        ? null
                        : "声明了 Mode=CanvasSwitch，但预制体上没有 Canvas 组件 —— 该面板将永远关不掉";
                case VisibilityMode.CanvasGroup:
                    // 缺 CanvasGroup 不算「不匹配」：Register 会补齐（见 EnsureCanvasGroup）
                    return null;
                case VisibilityMode.ToggleActive:
                    return null;
                default:
                    return "未知的 VisibilityMode: " + mode;
            }
        }

        /// <summary>
        /// 保证面板根上有一个 CanvasGroup。**由 UIManager.Register 调用** ——
        /// 阶段 3 的事件层要用 `CanvasGroup.interactable` 挡住模态面板下方的交互，
        /// 那不能依赖「恰好这个面板选了 CanvasGroup 模式」。
        ///
        /// 每次重新 GetComponent 而不用 Awake 的缓存：EditMode 下 Awake 不执行，
        /// 而本方法要能被 EditMode 测试直接调。
        /// </summary>
        internal CanvasGroup EnsureCanvasGroup()
        {
            var cg = GetComponent<CanvasGroup>();
            if (cg == null)
                cg = gameObject.AddComponent<CanvasGroup>();

            CanvasGroup = cg;
            return cg;
        }

        internal void ApplyVisibilityOn()
        {
            switch (Mode)
            {
                case VisibilityMode.ToggleActive:
                    gameObject.SetActive(true);
                    break;
                case VisibilityMode.CanvasSwitch:
                    if (Canvas != null) Canvas.enabled = true;
                    break;
                case VisibilityMode.CanvasGroup:
                    if (CanvasGroup != null)
                    {
                        CanvasGroup.alpha = 1f;
                        CanvasGroup.blocksRaycasts = true;
                    }
                    break;
            }
        }
    }
}
