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
        private Transform _originalParent;

        private void Awake()
        {
            _originalParent = transform;
            if (_ringRenderer == null)
                _ringRenderer = GetComponentInChildren<SpriteRenderer>();
            if (_ringRenderer != null)
                _ringRenderer.enabled = false;
        }

        /// <summary>re-parent 到目标脚下并显示</summary>
        public void AttachTo(Transform target, float yOffset)
        {
            if (_ringRenderer == null) return;
            transform.SetParent(target);
            transform.localPosition = new Vector3(0, yOffset, 0);
            transform.localRotation = Quaternion.identity;
            _ringRenderer.enabled = true;
        }

        /// <summary>re-parent 回玩家并隐藏</summary>
        public void Detach()
        {
            if (_ringRenderer != null)
                _ringRenderer.enabled = false;
            transform.SetParent(_originalParent);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
    }
}
