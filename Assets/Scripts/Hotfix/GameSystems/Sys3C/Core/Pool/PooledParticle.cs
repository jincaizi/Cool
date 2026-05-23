using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Pool
{
    [RequireComponent(typeof(ParticleSystem))]
    public class PooledParticle : MonoBehaviour
    {
        private ComponentPool<ParticleSystem> _pool;
        private ParticleSystem _ps;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            var main = _ps.main;
            main.stopAction = ParticleSystemStopAction.Callback;
        }

        public void SetPool(ComponentPool<ParticleSystem> pool)
        {
            _pool = pool;
        }

        private void OnParticleSystemStopped()
        {
            _pool?.Return(_ps);
        }
    }
}
