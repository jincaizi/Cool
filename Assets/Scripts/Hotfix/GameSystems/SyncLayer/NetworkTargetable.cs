using System;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>远程实体的只读 ITargetable：本地伤害链永远够不到它（不实现 IDamageable）。</summary>
    public sealed class NetworkTargetable : ITargetable
    {
        private float _hp;
        private float _maxHp;
        private bool _dead;
        private bool _removed;
        private Vector3 _lastPosition;

        public long EntityId { get; }
        public string DisplayName { get; private set; }
        public int Level => 1;
        public Sprite? Portrait => null;
        public float HPPercent => _maxHp > 0f ? Mathf.Clamp01(_hp / _maxHp) : 0f;
        public int CurrentHP => Mathf.Max(0, Mathf.CeilToInt(_hp));
        public int MaxHP => Mathf.Max(1, Mathf.CeilToInt(_maxHp));
        public Vector3 WorldPosition => _lastPosition;
        public float SelectionRingYOffset => 0.05f;

        public event Action<float, int, int>? OnHPChanged;
        public event Action? OnDeath;

        public NetworkTargetable(long entityId, string displayName)
        {
            EntityId = entityId;
            DisplayName = displayName;
        }

        public void SetVitals(float hp, float maxHp, bool dead)
        {
            bool hpChanged = !Mathf.Approximately(_hp, hp) || !Mathf.Approximately(_maxHp, maxHp);
            _hp = hp;
            _maxHp = maxHp;
            if (dead && !_dead) OnDeath?.Invoke();
            _dead = dead;
            if (hpChanged) OnHPChanged?.Invoke(HPPercent, CurrentHP, MaxHP);
        }

        public void SetDisplayName(string displayName)
        {
            if (!string.IsNullOrEmpty(displayName)) DisplayName = displayName;
        }

        public void SetPosition(Vector3 position) => _lastPosition = position;

        public void MarkRemoved()
        {
            if (_removed) return;
            _removed = true;
            if (!_dead) { _dead = true; OnDeath?.Invoke(); }
        }
    }
}
