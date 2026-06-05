using UnityEngine;

namespace Hotfix.GameSystems.Sys3C
{
    /// <summary>
    /// 唯一天地光环 — 视觉层
    /// 挂在玩家角色的 RingVisual 子物体上
    /// </summary>
    public class SelectionRing : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _ringRenderer;
        [SerializeField] private float _groundYOffset = 0.5f;
        [SerializeField] private float _displayScale = 1.2f;

        private Transform _originalParent;
        private Vector3 _originalLocalPos;
        private Quaternion _originalLocalRot;
        private Vector3 _originalLocalScale;

        private void Awake()
        {
            _originalParent = transform.parent;
            _originalLocalPos = transform.localPosition;
            _originalLocalRot = transform.localRotation;
            _originalLocalScale = transform.localScale;

            if (_ringRenderer == null)
                _ringRenderer = GetComponentInChildren<SpriteRenderer>();
            if (_ringRenderer != null)
                _ringRenderer.enabled = false;
        }

        /// <summary>re-parent 到目标模型脚下并显示，fineTuneOffset 来自怪物配置的 RingYOffset</summary>
        public void AttachTo(Transform target, float fineTuneOffset = 0f)
        {
            if (_ringRenderer == null) return;

            // Use target's root position as the base (model root = feet level),
            // then raise above ground to avoid terrain occlusion.
            float localY = _groundYOffset + fineTuneOffset;

            transform.SetParent(target);
            transform.localPosition = new Vector3(0, localY, 0);
            transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            transform.localScale = new Vector3(_displayScale, _displayScale, 1f);
            _ringRenderer.enabled = true;
        }

        /// <summary>re-parent 回玩家并隐藏</summary>
        public void Detach()
        {
            if (_ringRenderer != null)
                _ringRenderer.enabled = false;

            transform.SetParent(_originalParent);
            transform.localPosition = _originalLocalPos;
            transform.localRotation = _originalLocalRot;
            transform.localScale = _originalLocalScale;
        }
    }
}
