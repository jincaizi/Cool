using Hotfix.GameSystems.UI.Framework.Binding;

namespace Hotfix.GameSystems.UI.Panel.HUD
{
    /// <summary>
    /// ViewModel for HUD panel.
    /// </summary>
    public class HUDViewModel : ViewModelBase
    {
        private int _health;
        private int _maxHealth;
        private int _mana;
        private int _maxMana;
        private string _playerName;

        public int Health
        {
            get => _health;
            set
            {
                _health = value;
                SetProperty("Health", value);
            }
        }

        public int MaxHealth
        {
            get => _maxHealth;
            set
            {
                _maxHealth = value;
                SetProperty("MaxHealth", value);
            }
        }

        public int Mana
        {
            get => _mana;
            set
            {
                _mana = value;
                SetProperty("Mana", value);
            }
        }

        public int MaxMana
        {
            get => _maxMana;
            set
            {
                _maxMana = value;
                SetProperty("MaxMana", value);
            }
        }

        public string PlayerName
        {
            get => _playerName;
            set
            {
                _playerName = value;
                SetProperty("PlayerName", value);
            }
        }

        public float HealthPercent => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;
        public float ManaPercent => MaxMana > 0 ? (float)Mana / MaxMana : 0f;

        public override void Refresh()
        {
            // Simulate data refresh
            // In real game, fetch from character system
        }
    }
}