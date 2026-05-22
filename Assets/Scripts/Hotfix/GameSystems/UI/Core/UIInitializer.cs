using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public class UIInitializer : MonoBehaviour
    {
        [SerializeField] private PlayerHudPanel _playerHudPanel;
        [SerializeField] private TargetPanel _targetPanel;

        private async void Start()
        {
            var uiManager = UIManager.Instance;
            if (uiManager == null)
            {
                Debug.LogError("UIInitializer: UIManager.Instance is null");
                return;
            }

            if (_playerHudPanel != null)
            {
                uiManager.Register(_playerHudPanel);
                await uiManager.ShowAlwaysAsync("PlayerHudPanel");
            }

            if (_targetPanel != null)
            {
                uiManager.Register(_targetPanel);
            }
        }
    }
}
