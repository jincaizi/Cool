using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class AttackHeavyReadyView : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private GameObject _goldBorder;
        [SerializeField] private Color _defaultColor = Color.white;
        [SerializeField] private Color _readyColor = new Color(1f, 0.72f, 0.18f, 1f);

        public void SetReady(bool ready)
        {
            if (_icon != null) _icon.color = ready ? _readyColor : _defaultColor;
            if (_goldBorder != null) _goldBorder.SetActive(ready);
        }

        private void OnDisable() => SetReady(false);
    }
}
