using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityInput = UnityEngine.Input;

namespace Hotfix.GameSystems.Sys3C.Debug
{
    /// <summary>
    /// 调试窗口管理器 - 基于 UGUI 的运行时调试窗口
    /// </summary>
    public class DebugWindowManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject _windowRoot;
        [SerializeField] private Text _baseLayerText;
        [SerializeField] private Text _attackLayerText;
        [SerializeField] private Text _hitLayerText;
        [SerializeField] private Text _eventLogText;
        [SerializeField] private ScrollRect _eventLogScrollRect;

        [Header("Buttons")]
        [SerializeField] private Button _lockBaseButton;
        [SerializeField] private Button _lockAttackButton;
        [SerializeField] private Button _forceHitButton;
        [SerializeField] private Button _clearLogButton;

        [Header("Settings")]
        [SerializeField] private int _maxLogLines = 50;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F3;

        private bool _isVisible = true;
        private StateCoordinator _coordinator;
        private readonly List<string> _logLines = new();

        // 保存回调引用用于取消订阅
        private Action<StateChangedEvent> _onStateChanged;
        private Action<SkillActivatedEvent> _onSkillActivated;
        private Action<SkillCompletedEvent> _onSkillCompleted;
        private Action<DamageEvent> _onDamage;

        private void Awake()
        {
            // 默认显示窗口
            if (_windowRoot != null)
            {
                _windowRoot.SetActive(_isVisible);
            }

            SetupButtons();
        }

        private void Update()
        {
            // 快捷键切换
            if (UnityInput.GetKeyDown(_toggleKey))
            {
                Toggle();
            }
        }

        /// <summary>
        /// 显示/隐藏窗口
        /// </summary>
        public void Toggle()
        {
            _isVisible = !_isVisible;
            if (_windowRoot != null)
            {
                _windowRoot.SetActive(_isVisible);
            }
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public void Initialize(StateCoordinator coordinator)
        {
            _coordinator = coordinator;

            // 创建回调引用用于取消订阅
            _onStateChanged = OnStateChanged;
            _onSkillActivated = OnSkillActivated;
            _onSkillCompleted = OnSkillCompleted;
            _onDamage = OnDamage;

            // 订阅事件
            EventBus.Subscribe(_onStateChanged);
            EventBus.Subscribe(_onSkillActivated);
            EventBus.Subscribe(_onSkillCompleted);
            EventBus.Subscribe(_onDamage);

            UpdateDisplay();
        }

        /// <summary>
        /// 添加日志
        /// </summary>
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            var timestamp = Time.time.ToString("F2");
            var levelTag = level switch
            {
                LogLevel.Warning => "[WARN]",
                LogLevel.Error => "[ERR]",
                LogLevel.Debug => "[DBG]",
                _ => "[INFO]"
            };
            var line = $"[{timestamp}] {levelTag} {message}";
            _logLines.Add(line);

            // 限制行数
            while (_logLines.Count > _maxLogLines)
            {
                _logLines.RemoveAt(0);
            }

            UpdateLogDisplay();

            // 同时记录到 StateLogger
            StateLogger.Log("DebugUI", message, level);
        }

        /// <summary>
        /// 更新状态显示
        /// </summary>
        public void UpdateDisplay()
        {
            if (_coordinator == null) return;

            // 更新层状态
            if (_baseLayerText != null)
            {
                var baseState = _coordinator.ActiveLayer == LayerType.Base ? "Active" : "Inactive";
                _baseLayerText.text = $"Base: {baseState}";
            }

            if (_attackLayerText != null)
            {
                var attackState = _coordinator.ActiveLayer == LayerType.Attack ? "Active" : "Inactive";
                _attackLayerText.text = $"Attack: {attackState}";
            }

            if (_hitLayerText != null)
            {
                var hitState = _coordinator.ActiveLayer == LayerType.Hit ? "Active" : "Inactive";
                _hitLayerText.text = $"Hit: {hitState}";
            }
        }

        private void SetupButtons()
        {
            if (_lockBaseButton != null)
            {
                _lockBaseButton.onClick.AddListener(() => Log("Base layer lock toggled"));
            }

            if (_lockAttackButton != null)
            {
                _lockAttackButton.onClick.AddListener(() => Log("Attack layer lock toggled"));
            }

            if (_forceHitButton != null)
            {
                _forceHitButton.onClick.AddListener(OnForceHit);
            }

            if (_clearLogButton != null)
            {
                _clearLogButton.onClick.AddListener(OnClearLog);
            }
        }

        private void OnForceHit()
        {
            Log("Force Hit triggered!", LogLevel.Warning);
            var damage = new DamageEvent(0, 0, 10f);
            _coordinator?.HandleDamage(damage);
        }

        private void OnClearLog()
        {
            _logLines.Clear();
            UpdateLogDisplay();
            StateLogger.Clear();
        }

        private void UpdateLogDisplay()
        {
            if (_eventLogText != null)
            {
                _eventLogText.text = string.Join("\n", _logLines);
            }

            // 滚动到底部
            if (_eventLogScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                _eventLogScrollRect.verticalNormalizedPosition = 0;
            }
        }

        private void OnStateChanged(StateChangedEvent evt)
        {
            Log($"State: {evt.Layer} {evt.PreviousState} -> {evt.CurrentState}");
            UpdateDisplay();
        }

        private void OnSkillActivated(SkillActivatedEvent evt)
        {
            Log($"Skill: {evt.SkillName} activated (ID: {evt.SkillId})");
        }

        private void OnSkillCompleted(SkillCompletedEvent evt)
        {
            var status = evt.WasInterrupted ? "interrupted" : "completed";
            Log($"Skill {evt.SkillId} {status}");
            UpdateDisplay();
        }

        private void OnDamage(DamageEvent evt)
        {
            Log($"Damage: {evt.Damage} (Crit: {evt.IsCritical})", LogLevel.Warning);
        }

        private void OnDestroy()
        {
            // 取消订阅
            if (_onStateChanged != null)
                EventBus.Unsubscribe(_onStateChanged);
            if (_onSkillActivated != null)
                EventBus.Unsubscribe(_onSkillActivated);
            if (_onSkillCompleted != null)
                EventBus.Unsubscribe(_onSkillCompleted);
            if (_onDamage != null)
                EventBus.Unsubscribe(_onDamage);
        }
    }
}