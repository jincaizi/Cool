using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    public class StatusEffectManager : MonoBehaviour
    {
        public static StatusEffectManager Instance { get; private set; }

        private readonly StatusEffectRegistry _registry = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            _registry.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Apply(IEffectTarget target, EffectData effect, IEffectTarget caster)
            => _registry.Apply(target, effect, caster);

        public void Remove(IEffectTarget target, string effectId)
            => _registry.Remove(target, effectId);

        public void RemoveAll(IEffectTarget target)
            => _registry.RemoveAll(target);
    }
}
