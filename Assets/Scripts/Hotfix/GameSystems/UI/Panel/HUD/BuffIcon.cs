using Hotfix.GameSystems.Sys3C.Core.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class BuffIcon : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private Image _overlay;
        [SerializeField] private Image _border;

        private float _expireTime;
        private float _duration;
        private bool _isPermanent;

        public void SetBuff(BuffInfo buff)
        {
            _isPermanent = buff.RemainingTime < 0;
            if (_icon != null && buff.Icon != null)
                _icon.sprite = buff.Icon;

            if (_border != null)
                _border.color = buff.IsDebuff
                    ? new Color(0.8f, 0.2f, 0.2f, 1f)
                    : new Color(0.2f, 0.8f, 0.2f, 1f);

            _duration = buff.Duration;
            _expireTime = _isPermanent ? float.MaxValue : Time.time + buff.RemainingTime;

            if (_overlay != null)
            {
                _overlay.gameObject.SetActive(!_isPermanent);
                _overlay.fillAmount = 0f;
            }
        }

        private void Update()
        {
            if (_isPermanent || _overlay == null || _duration <= 0) return;
            float remaining = Mathf.Max(0f, _expireTime - Time.time);
            _overlay.fillAmount = 1f - remaining / _duration;
        }
    }
}
