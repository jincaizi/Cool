using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 角色选择面板：男女选择（上次选择高亮，默认男），确认后持久化并广播事件。
    /// 预览体为场景烘焙的剥离模型（本面板不持有预览引用，无 GetComponent）。
    /// </summary>
    public class CharacterSelectPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Popup;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        public override string PanelId => "CharacterSelectPanel";

        [SerializeField] private Button _maleButton;
        [SerializeField] private Button _femaleButton;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private GameObject _maleHighlight;
        [SerializeField] private GameObject _femaleHighlight;

        private CharacterGender _selected;

        protected override void Awake()
        {
            base.Awake();
            if (_maleButton != null) _maleButton.onClick.AddListener(() => Select(CharacterGender.Male));
            if (_femaleButton != null) _femaleButton.onClick.AddListener(() => Select(CharacterGender.Female));
            if (_confirmButton != null) _confirmButton.onClick.AddListener(Confirm);
        }

        internal override void OnShow()
        {
            base.OnShow();
            _selected = CharacterSelectionService.Load();
            RefreshHighlight();
        }

        private void Select(CharacterGender gender)
        {
            _selected = gender;
            RefreshHighlight();
        }

        private void RefreshHighlight()
        {
            if (_maleHighlight != null) _maleHighlight.SetActive(_selected == CharacterGender.Male);
            if (_femaleHighlight != null) _femaleHighlight.SetActive(_selected == CharacterGender.Female);
        }

        private void Confirm()
        {
            CharacterSelectionService.Save(_selected);
            EventBus.Emit(new CharacterSelectedEvent(_selected));
            CloseSelf();
        }

        private async void CloseSelf()
        {
            if (UIManager.Instance != null)
                await UIManager.Instance.CloseAsync(PanelId);
        }
    }
}
