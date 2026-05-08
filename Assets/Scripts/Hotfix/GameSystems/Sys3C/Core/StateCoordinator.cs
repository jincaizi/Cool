using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Sys3C.Core
{
    public class StateCoordinator
    {
        private readonly object _baseFSM;
        private readonly object _hitFSM;

        private LayerType _activeLayer = LayerType.Base;
        private LayerType _lockedLayer = LayerType.Base;
        private float _resistance = 100f;

        public LayerType ActiveLayer => _activeLayer;
        public bool CanMove => _activeLayer != LayerType.Hit;
        public bool CanAttack => _activeLayer != LayerType.Hit;

        public bool IsImmune
        {
            get
            {
                var hitProp = _hitFSM?.GetType().GetProperty("HasSuperArmor");
                return (bool)(hitProp?.GetValue(_hitFSM) ?? false);
            }
        }

        public StateCoordinator(object baseFSM, object hitFSM)
        {
            _baseFSM = baseFSM;
            _hitFSM = hitFSM;
        }

        public void Initialize()
        {
        }

        public void Update(float deltaTime)
        {
        }

        public bool TryRequestJump()
        {
            if (_activeLayer == LayerType.Hit) return false;
            if (_activeLayer == LayerType.Attack) return false;

            EventBus.Emit(JumpEvent.Start);
            return true;
        }

        public void HandleDamage(DamageEvent damage)
        {
            if (IsImmune) return;

            _resistance -= damage.Damage * 0.5f;
            if (_resistance < 0) _resistance = 0;

            EventBus.Emit(damage);
            EventBus.Emit(new HitReceivedEvent());
        }

        public void HandleDeath()
        {
            SetActiveLayer(LayerType.Hit);

            var lockMethod = _baseFSM?.GetType().GetMethod("LockState");
            lockMethod?.Invoke(_baseFSM, new object[] { 0 });

            LockLayer(LayerType.Base);
        }

        public void HandleResurrect()
        {
            _resistance = 100f;
            UnlockAndReturnToBase();
        }

        public float GetResistance() => _resistance;

        public void RestoreResistance(float amount)
        {
            _resistance = Mathf.Min(_resistance + amount, 100f);
        }

        public Vector3 GetKnockbackDisplacement()
        {
            var method = _hitFSM?.GetType().GetMethod("GetKnockbackDisplacement");
            return (Vector3)(method?.Invoke(_hitFSM, null) ?? Vector3.zero);
        }

        public bool IsInAirHit()
        {
            var stateProp = _hitFSM?.GetType().GetProperty("CurrentState");
            var state = (int)(stateProp?.GetValue(_hitFSM) ?? 0);
            return state == 3; // Launched
        }

        public string GetActiveStateDescription()
        {
            var baseState = _baseFSM?.GetType().GetProperty("CurrentState")?.GetValue(_baseFSM)?.ToString() ?? "null";
            var hitState = _hitFSM?.GetType().GetProperty("CurrentState")?.GetValue(_hitFSM)?.ToString() ?? "null";
            return $"[Layer: {_activeLayer}] Base={baseState}, Hit={hitState}";
        }

        public string GetActiveState()
        {
            if (_activeLayer == LayerType.Base)
            {
                return _baseFSM?.GetType().GetProperty("CurrentState")?.GetValue(_baseFSM)?.ToString() ?? "null";
            }
            else if (_activeLayer == LayerType.Hit)
            {
                return _hitFSM?.GetType().GetProperty("CurrentState")?.GetValue(_hitFSM)?.ToString() ?? "null";
            }
            return "Unknown";
        }

        public void UnlockAndReturnToBase()
        {
            _lockedLayer = LayerType.Base;
            SetActiveLayer(LayerType.Base);

            var unlockMethod = _baseFSM?.GetType().GetMethod("Unlock");
            unlockMethod?.Invoke(_baseFSM, new object[] { 0 });

            EventBus.Emit(new LayerUnlockedEvent(LayerType.Base));
        }

        public void SetActiveLayer(LayerType layer)
        {
            if (_activeLayer != layer)
            {
                var previous = _activeLayer;
                _activeLayer = layer;
                EventBus.Emit(new StateChangedEvent(layer, previous.ToString(), layer.ToString()));
            }
        }

        public void SetAttackLayerActive()
        {
            LockLayer(LayerType.Base);
            SetActiveLayer(LayerType.Attack);
        }

        private void LockLayer(LayerType layer)
        {
            if (_lockedLayer != layer)
            {
                _lockedLayer = layer;
                EventBus.Emit(new LayerLockedEvent(layer, true));
            }
        }
    }
}
