using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public sealed class DefendHaloVfx : MonoBehaviour
    {
        private static readonly int Tint = Shader.PropertyToID("_Color");

        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer _halo;
        [SerializeField] private Renderer _dome;

        private MaterialPropertyBlock _properties;
        private float _alpha;
        private float _blockPulse;
        private bool _held;
        private bool _useRemoteState;
        private bool _remoteHeld;

        internal void SetRemoteHeld(bool held)
        {
            _useRemoteState = true;
            _remoteHeld = held;
        }

        private void OnEnable() => EventBus.Subscribe<BlockSucceededEvent>(OnBlock);

        private void OnDisable()
        {
            EventBus.Unsubscribe<BlockSucceededEvent>(OnBlock);
            _held = false;
            _alpha = 0f;
            _blockPulse = 0f;
            if (_halo != null) _halo.enabled = false;
            if (_dome != null) _dome.enabled = false;
        }

        private void LateUpdate()
        {
            if (_halo == null || _animator == null) return;
            _held = _useRemoteState
                ? _remoteHeld
                : _animator.isActiveAndEnabled && _animator.GetBool(AnimHashes.IsDefending);
            _alpha = Mathf.MoveTowards(_alpha, _held ? 1f : 0f, Time.deltaTime * 12f);
            _blockPulse = Mathf.MoveTowards(_blockPulse, 0f, Time.deltaTime * 7f);
            _halo.enabled = _alpha > 0f;
            if (_dome != null) _dome.enabled = _halo.enabled;
            if (!_halo.enabled) return;

            _properties ??= new MaterialPropertyBlock();
            _properties.SetColor(Tint, new Color(1f, 1f, 1f, _alpha * (0.82f + 0.18f * _blockPulse)));
            _halo.SetPropertyBlock(_properties);
            if (_dome != null) _dome.SetPropertyBlock(_properties);
        }

        private void OnBlock(BlockSucceededEvent hit)
        {
            if (!_held) return;
            if (hit.EntityId == gameObject.GetInstanceID()
                || (transform.parent != null && hit.EntityId == transform.parent.gameObject.GetInstanceID()))
                _blockPulse = 1f;
        }
    }
}
