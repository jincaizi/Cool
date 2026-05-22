using System.Collections.Generic;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class PlayerHudPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Base;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        public override string PanelId => "PlayerHudPanel";

        [SerializeField] private Image _portrait;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _levelText;
        [SerializeField] private Image _hpFill;
        [SerializeField] private TMP_Text _hpOverlay;
        [SerializeField] private Image _mpFill;
        [SerializeField] private TMP_Text _mpOverlay;
        [SerializeField] private Transform _buffContainer;
        [SerializeField] private GameObject _buffIconPrefab;

        private IPlayerStatsProvider _provider;
        private readonly List<GameObject> _activeBuffIcons = new();

        public void Bind(IPlayerStatsProvider provider)
        {
            Unbind();
            _provider = provider;

            if (_nameText != null) _nameText.text = provider.Name;
            if (_levelText != null) _levelText.text = $"Lv.{provider.Level}";
            if (_portrait != null && provider.Portrait != null) _portrait.sprite = provider.Portrait;

            UpdateHP(provider.HPPercent, provider.CurrentHP, provider.MaxHP);
            UpdateMP(provider.MPPercent, provider.CurrentMP, provider.MaxMP);
            UpdateBuffs(provider.ActiveBuffs);

            provider.OnHPChanged += UpdateHP;
            provider.OnMPChanged += UpdateMP;
            provider.OnBuffsChanged += UpdateBuffs;
        }

        public void Unbind()
        {
            if (_provider != null)
            {
                _provider.OnHPChanged -= UpdateHP;
                _provider.OnMPChanged -= UpdateMP;
                _provider.OnBuffsChanged -= UpdateBuffs;
                _provider = null;
            }
            ClearBuffIcons();
        }

        private void UpdateHP(float percent, int current, int max)
        {
            if (_hpFill != null) _hpFill.fillAmount = percent;
            if (_hpOverlay != null) _hpOverlay.text = $"{current}/{max}";
        }

        private void UpdateMP(float percent, int current, int max)
        {
            if (_mpFill != null) _mpFill.fillAmount = percent;
            if (_mpOverlay != null) _mpOverlay.text = $"{current}/{max}";
        }

        private void UpdateBuffs(BuffInfo[] buffs)
        {
            ClearBuffIcons();
            if (buffs == null || buffs.Length == 0 || _buffContainer == null || _buffIconPrefab == null) return;

            foreach (var buff in buffs)
            {
                var go = GetOrCreateBuffIcon();
                go.SetActive(true);
                var icon = go.GetComponent<BuffIcon>();
                if (icon == null) icon = go.AddComponent<BuffIcon>();
                icon.SetBuff(buff);
            }
        }

        private GameObject GetOrCreateBuffIcon()
        {
            foreach (var go in _activeBuffIcons)
            {
                if (!go.activeSelf) return go;
            }
            var newGo = Instantiate(_buffIconPrefab, _buffContainer);
            _activeBuffIcons.Add(newGo);
            return newGo;
        }

        private void ClearBuffIcons()
        {
            foreach (var go in _activeBuffIcons)
            {
                if (go != null) go.SetActive(false);
            }
        }

        private new void OnDestroy()
        {
            Unbind();
        }
    }
}
