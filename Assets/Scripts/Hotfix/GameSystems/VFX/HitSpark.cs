using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 火花粒子实例：播放一次后由 ParticleSystemStopAction.Callback 自动置为 inactive（池复用，无轮询）
    /// </summary>
    public class HitSpark : MonoBehaviour
    {
        private ParticleSystem _ps;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
        }

        public void Play(Vector3 worldPos, Color color, float scale)
        {
            transform.position = worldPos;
            transform.localScale = Vector3.one * scale;
            var main = _ps.main;
            main.startColor = color;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);  // 池槽位复用前先清
            gameObject.SetActive(true);
            _ps.Play();
        }

        private void OnParticleSystemStopped()
        {
            gameObject.SetActive(false);
        }
    }
}
