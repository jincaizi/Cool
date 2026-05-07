using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class NameplateManager : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _fontAsset;
        [SerializeField] private Material _fontMaterial;
        [SerializeField] private float _cullDistance = 50f;

        private readonly Dictionary<Transform, TextMeshPro> _nameplates = new();
        private readonly List<Transform> _deadKeys = new();
        private Camera _camera;

        public static NameplateManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _camera = Camera.main;
        }

        public void Register(Transform owner, string displayName, Color color)
        {
            if (_nameplates.ContainsKey(owner)) return;

            var go = new GameObject($"Nameplate_{owner.name}");
            var tmp = go.AddComponent<TextMeshPro>();
            if (_fontAsset != null) tmp.font = _fontAsset;
            if (_fontMaterial != null) tmp.fontMaterial = _fontMaterial;

            tmp.text = displayName;
            tmp.color = color;
            tmp.fontSize = 3.5f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            _nameplates[owner] = tmp;
        }

        public void Unregister(Transform owner)
        {
            if (!_nameplates.TryGetValue(owner, out var tmp)) return;
            if (tmp != null) Destroy(tmp.gameObject);
            _nameplates.Remove(owner);
        }

        public void UpdateName(Transform owner, string newName)
        {
            if (_nameplates.TryGetValue(owner, out var tmp))
                tmp.text = newName;
        }

        public void SetVisible(Transform owner, bool visible)
        {
            if (_nameplates.TryGetValue(owner, out var tmp))
                tmp.enabled = visible;
        }

        private void LateUpdate()
        {
            if (_camera == null) { _camera = Camera.main; if (_camera == null) return; }

            _deadKeys.Clear();
            var camPos = _camera.transform.position;
            var camRot = _camera.transform.rotation;

            foreach (var kv in _nameplates)
            {
                var owner = kv.Key;
                var tmp = kv.Value;

                if (owner == null || tmp == null)
                {
                    _deadKeys.Add(owner);
                    continue;
                }

                var dist = Vector3.Distance(camPos, owner.position);
                if (dist > _cullDistance)
                {
                    tmp.enabled = false;
                    continue;
                }

                tmp.enabled = true;

                // Follow position + offset
                var tag = owner.GetComponent<NameplateTag>();
                var offset = tag != null ? tag.Offset : Vector3.up * 2.5f;
                tmp.transform.position = owner.position + offset;

                // Billboard
                tmp.transform.rotation = camRot;
            }

            foreach (var k in _deadKeys)
                _nameplates.Remove(k);
        }
    }
}
