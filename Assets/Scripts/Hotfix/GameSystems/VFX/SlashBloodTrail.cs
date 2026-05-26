using System.Collections;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class SlashBloodTrail : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 3f;
        [SerializeField] private float _moveDistance = 0.5f;
        [SerializeField] private float _fadeDelay = 0.3f;

        private TrailRenderer _trail;
        private ComponentPool<SlashBloodTrail> _pool;
        private Coroutine _activeRoutine;

        private void Awake()
        {
            _trail = GetComponent<TrailRenderer>();
        }

        public void SetPool(ComponentPool<SlashBloodTrail> pool)
        {
            _pool = pool;
        }

        public void Activate(Vector3 startPos, Vector3 direction)
        {
            if (_activeRoutine != null)
                StopCoroutine(_activeRoutine);

            transform.position = startPos;
            _trail.Clear();
            _trail.emitting = true;

            _activeRoutine = StartCoroutine(SlashRoutine(direction.normalized));
        }

        private IEnumerator SlashRoutine(Vector3 direction)
        {
            float traveled = 0f;
            while (traveled < _moveDistance)
            {
                float step = _moveSpeed * Time.deltaTime;
                if (traveled + step > _moveDistance)
                    step = _moveDistance - traveled;
                transform.position += direction * step;
                traveled += step;
                yield return null;
            }

            _trail.emitting = false;
            yield return new WaitForSeconds(_fadeDelay);

            _activeRoutine = null;
            _pool?.Return(this);
        }
    }
}
