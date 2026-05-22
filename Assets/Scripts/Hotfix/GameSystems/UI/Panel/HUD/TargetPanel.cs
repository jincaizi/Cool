using System.Collections.Generic;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class TargetPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Base;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        public override string PanelId => "TargetPanel";

        [SerializeField] private Image _portrait;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _levelText;
        [SerializeField] private Image _hpFill;
        [SerializeField] private TMP_Text _hpOverlay;
        [SerializeField] private Image _mpFill;
        [SerializeField] private TMP_Text _mpOverlay;
        [SerializeField] private Transform _buffContainer;
        [SerializeField] private GameObject _buffIconPrefab;
        [SerializeField] private GameObject _contentRoot;

        private ITargetable _currentTarget;
        private ITargetStatsProvider _targetStats;
        private readonly List<GameObject> _activeBuffIcons = new();

        public void Bind(ITargetable target)
        {
            Clear();
            _currentTarget = target;
            _targetStats = target as ITargetStatsProvider;

            if (_nameText != null) _nameText.text = target.DisplayName;
            if (_levelText != null) _levelText.text = $"Lv.{target.Level}";
            if (_portrait != null && target.Portrait != null) _portrait.sprite = target.Portrait;

            UpdateHP(target.HPPercent, target.CurrentHP, target.MaxHP);
            target.OnHPChanged += UpdateHP;
            target.OnDeath += OnTargetDeath;

            if (_targetStats != null)
            {
                UpdateMP(_targetStats.MPPercent, _targetStats.CurrentMP, _targetStats.MaxMP);
                UpdateBuffs(_targetStats.ActiveBuffs);
                _targetStats.OnMPChanged += UpdateMP;
                _targetStats.OnBuffsChanged += UpdateBuffs;
            }

            if (_contentRoot != null) _contentRoot.SetActive(true);
        }

        public void Clear()
        {
            if (_currentTarget != null)
            {
                _currentTarget.OnHPChanged -= UpdateHP;
                _currentTarget.OnDeath -= OnTargetDeath;
                _currentTarget = null;
            }
            if (_targetStats != null)
            {
                _targetStats.OnMPChanged -= UpdateMP;
                _targetStats.OnBuffsChanged -= UpdateBuffs;
                _targetStats = null;
            }
            ClearBuffIcons();
            if (_contentRoot != null) _contentRoot.SetActive(false);
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

        private void OnTargetDeath()
        {
            Clear();
            UIManager.Instance?.HideAlwaysAsync(PanelId);
        }

        private new void OnDestroy()
        {
            Clear();
        }
    }
}
