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
        public UIAnimation Animation { get; private set; }
        public bool IsVisible { get; private set; }

        protected virtual void Awake()
        {
            Canvas = GetComponent<Canvas>();
            CanvasGroup = GetComponent<CanvasGroup>();
            Animation = GetComponent<UIAnimation>();
        }

        internal virtual void OnPreShow() { }
        internal virtual Sequence PlayShowAnimation()
        {
            return Animation != null ? Animation.PlayShow() : null;
        }
        internal virtual void OnShow() { }
        internal virtual void OnHide() { }
        internal virtual Sequence PlayHideAnimation()
        {
            return Animation != null ? Animation.PlayHide() : null;
        }

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
