using System;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 角色选择面板：男女选择（上次选择高亮，默认男）+ 网络模式选择（单机/联网），
    /// 确认后持久化并广播事件。预览体为场景烘焙的剥离模型（本面板不持有预览引用，无 GetComponent）。
    /// </summary>
    public class CharacterSelectPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Popup;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        public override string PanelId => "CharacterSelectPanel";

        [SerializeField] private Button _maleButton;
        [SerializeField] private Button _femaleButton;
        [SerializeField] private Button _soloButton;
        [SerializeField] private Button _netButton;
        [SerializeField] private Button _confirmButton;
        /// <summary>选中按钮图片高亮色</summary>
        [SerializeField] private Color _selectedColor = new Color(1f, 0.84f, 0.36f);
        /// <summary>未选中按钮图片颜色</summary>
        [SerializeField] private Color _normalColor = new Color(0.62f, 0.62f, 0.62f);

        /// <summary>选人切换事件（场景侧 SelectStageController 用于切换预览体）</summary>
        public event Action<CharacterGender> OnSelectionChanged;
        /// <summary>面板完全关闭后触发（用于触发游戏场景加载）</summary>
        public event Action OnClosed;

        private CharacterGender _selected;
        private NetworkMode _mode;
        private bool _closing;   // 重入保护：防双击确认导致重复关闭/重复加载

        protected override void Awake()
        {
            base.Awake();
            if (_maleButton != null) _maleButton.onClick.AddListener(() => Select(CharacterGender.Male));
            if (_femaleButton != null) _femaleButton.onClick.AddListener(() => Select(CharacterGender.Female));
            if (_soloButton != null) _soloButton.onClick.AddListener(() => SelectMode(NetworkMode.SinglePlayer));
            if (_netButton != null) _netButton.onClick.AddListener(() => SelectMode(NetworkMode.Online));
            if (_confirmButton != null) _confirmButton.onClick.AddListener(Confirm);
        }

        internal override void OnShow()
        {
            base.OnShow();
            _selected = CharacterSelectionService.Load();
            _mode = NetworkModeService.Load();
            RefreshHighlight();
        }

        private void Select(CharacterGender gender)
        {
            _selected = gender;
            RefreshHighlight();
            OnSelectionChanged?.Invoke(gender);
        }

        private void SelectMode(NetworkMode mode)
        {
            _mode = mode;
            RefreshHighlight();
        }

        private void RefreshHighlight()
        {
            SetButtonColor(_maleButton, _selected == CharacterGender.Male);
            SetButtonColor(_femaleButton, _selected == CharacterGender.Female);
            SetButtonColor(_soloButton, _mode == NetworkMode.SinglePlayer);
            SetButtonColor(_netButton, _mode == NetworkMode.Online);
        }

        private void SetButtonColor(Button button, bool selected)
        {
            if (button == null || button.image == null) return;
            button.image.color = selected ? _selectedColor : _normalColor;
        }

        private void Confirm()
        {
            if (_closing) return;
            _closing = true;
            CharacterSelectionService.Save(_selected);
            NetworkModeService.Save(_mode);
            EventBus.Emit(new CharacterSelectedEvent(_selected));
            CloseSelf();
        }

        private async void CloseSelf()
        {
            if (UIManager.Instance != null)
                await UIManager.Instance.CloseAsync(PanelId);
            OnClosed?.Invoke();
        }
    }
}
