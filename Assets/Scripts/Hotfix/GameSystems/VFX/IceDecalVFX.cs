using System.Collections.Generic;
using DG.Tweening;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class IceDecalVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private GameObject _decalPrefab;
        [SerializeField] private float _duration = 3f;
        [SerializeField] private float _fadeDuration = 0.5f;

        private readonly Stack<GameObject> _pool = new();
        private readonly List<ActiveDecal> _active = new();

        private class ActiveDecal
        {
            public GameObject Root;
            public Material Mat;
            public Tween FadeTween;
            public float SpawnTime;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<SkillReleasedEvent>(OnReleased);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillReleasedEvent>(OnReleased);
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0) return true;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnReleased(SkillReleasedEvent e)
        {
            if (!e.IsFullCharge || !WatchesSkill(e.SkillId)) return;
            SpawnDecal();
        }

        private void SpawnDecal()
        {
            if (_decalPrefab == null) return;

            Vector3 pos = transform.position;
            pos.y = 0.01f;

            GameObject decal;
            if (_pool.Count > 0)
            {
                decal = _pool.Pop();
                decal.SetActive(true);
                decal.transform.position = pos;
            }
            else
            {
                decal = Instantiate(_decalPrefab, pos, Quaternion.Euler(90f, 0f, 0f));
            }

            var renderer = decal.GetComponent<Renderer>();
            Material mat = null;
            if (renderer != null)
            {
                mat = renderer.material;
                var c = mat.color;
                c.a = 1f;
                mat.color = c;
            }

            var entry = new ActiveDecal { Root = decal, Mat = mat, SpawnTime = Time.time };

            if (mat != null)
            {
                entry.FadeTween = mat.DOFade(0f, _fadeDuration)
                    .SetDelay(_duration - _fadeDuration)
                    .OnComplete(() => ReturnToPool(entry));
            }

            _active.Add(entry);
        }

        private void ReturnToPool(ActiveDecal entry)
        {
            _active.Remove(entry);
            entry.FadeTween?.Kill();
            entry.Root.SetActive(false);
            _pool.Push(entry.Root);
        }

        private void OnDestroy()
        {
            foreach (var entry in _active)
            {
                entry.FadeTween?.Kill();
                Destroy(entry.Root);
            }
            while (_pool.Count > 0)
                Destroy(_pool.Pop());
        }
    }
}
