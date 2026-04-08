using Hotfix.GameSystems.UI.Framework.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Panel.HUD
{
    /// <summary>
    /// HUD panel example.
    /// Shows player health, mana, and name.
    /// </summary>
    public class HUDPanel : UIPanel
    {
        [Header("HUD References")]
        [SerializeField] private Slider _healthSlider;
        [SerializeField] private Slider _manaSlider;
        [SerializeField] private Text _healthText;
        [SerializeField] private Text _manaText;
        [SerializeField] private Text _nameText;

        private HUDViewModel _viewModel;

        protected override string PrefabPath => "";
        protected override int Layer => UIConst.Layer_Base;

        protected override void Awake()
        {
            base.Awake();

            // Create ViewModel
            _viewModel = new HUDViewModel();
        }

        public override void OnShow(params object[] args)
        {
            base.OnShow(args);

            // Bind
            Bind(_viewModel);

            // Setup bindings
            RegisterBinding("Health", () => _viewModel.Health, val => _healthText.text = $"{_viewModel.Health}/{_viewModel.MaxHealth}");
            RegisterBinding("Mana", () => _viewModel.Mana, val => _manaText.text = $"{_viewModel.Mana}/{_viewModel.MaxMana}");
            RegisterBinding("PlayerName", () => _viewModel.PlayerName, val => _nameText.text = _viewModel.PlayerName?.ToString() ?? "");

            // Subscribe to percent changes
            _viewModel.Subscribe("Health", () =>
            {
                if (_healthSlider != null)
                    _healthSlider.value = _viewModel.HealthPercent;
            });

            _viewModel.Subscribe("Mana", () =>
            {
                if (_manaSlider != null)
                    _manaSlider.value = _viewModel.ManaPercent;
            });

            // Refresh
            RefreshBindings();

            // Demo data
            _viewModel.MaxHealth = 100;
            _viewModel.Health = 75;
            _viewModel.MaxMana = 100;
            _viewModel.Mana = 50;
            _viewModel.PlayerName = "Player1";
        }

        public override void OnHide()
        {
            base.OnHide();
            Unbind();
        }

        private void OnDestroy()
        {
            _viewModel?.Dispose();
        }
    }
}