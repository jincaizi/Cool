using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.Nameplate
{
    public class NameplateRenderer
    {
        private readonly NameplateSettings _settings;
        private readonly Transform _canvasTransform;
        private readonly Stack<GameObject> _pool = new();
        private readonly Dictionary<int, DisplayEntry> _entries = new();

        private struct DisplayEntry
        {
            public Transform Owner;
            public GameObject Root;
            public TMP_Text NameText;
            public Image ClassIcon;
            public NameplateConfig Config;
        }

        public NameplateRenderer(NameplateSettings settings, Transform canvasTransform)
        {
            _settings = settings;
            _canvasTransform = canvasTransform;
        }

        public void Register(int entityId, Transform owner, NameplateConfig config)
        {
            if (_entries.ContainsKey(entityId)) return;

            var root = Rent();
            var nameText = root.transform.Find("NameText").GetComponent<TextMeshProUGUI>();
            var classIcon = root.transform.Find("ClassIcon").GetComponent<Image>();

            nameText.text = config.DisplayName;
            nameText.color = config.NameColor;

            if (config.ClassIcon != null)
            {
                classIcon.sprite = config.ClassIcon;
                classIcon.enabled = true;
            }
            else
            {
                classIcon.enabled = false;
            }

            _entries[entityId] = new DisplayEntry
            {
                Owner = owner,
                Root = root,
                NameText = nameText,
                ClassIcon = classIcon,
                Config = config
            };
        }

        public void Unregister(int entityId)
        {
            if (!_entries.TryGetValue(entityId, out var entry)) return;
            Return(entry);
            _entries.Remove(entityId);
        }

        public void UpdateName(int entityId, string newName)
        {
            if (_entries.TryGetValue(entityId, out var entry))
                entry.NameText.text = newName;
        }

        public void SetVisible(int entityId, bool visible)
        {
            if (_entries.TryGetValue(entityId, out var entry))
                entry.Root.SetActive(visible);
        }

        public void Tick(Camera camera)
        {
            if (camera == null) return;

            var camPos = camera.transform.position;
            var deadIds = new List<int>();

            foreach (var kv in _entries)
            {
                var id = kv.Key;
                var entry = kv.Value;

                if (entry.Owner == null || entry.Root == null)
                {
                    deadIds.Add(id);
                    continue;
                }

                var dist = Vector3.Distance(camPos, entry.Owner.position);
                var cullEnd = _settings.CullDistance;

                if (dist > cullEnd)
                {
                    entry.Root.SetActive(false);
                    continue;
                }

                entry.Root.SetActive(true);

                var worldPos = entry.Owner.position + Vector3.up * _settings.VerticalOffset;
                var screenPos = camera.WorldToScreenPoint(worldPos);
                if (screenPos.z > 0)
                    entry.Root.transform.position = screenPos;

                float alpha = dist > _settings.FadeStartDistance
                    ? 1f - Mathf.Clamp01((dist - _settings.FadeStartDistance) / (cullEnd - _settings.FadeStartDistance))
                    : 1f;

                var txtColor = entry.NameText.color;
                txtColor.a = alpha;
                entry.NameText.color = txtColor;

                if (entry.ClassIcon.enabled)
                {
                    var iconColor = entry.ClassIcon.color;
                    iconColor.a = alpha;
                    entry.ClassIcon.color = iconColor;
                }
            }

            foreach (var id in deadIds)
            {
                if (_entries.TryGetValue(id, out var entry))
                    Return(entry);
                _entries.Remove(id);
            }
        }

        public void Cleanup()
        {
            foreach (var kv in _entries)
                if (kv.Value.Root != null) Object.Destroy(kv.Value.Root);
            _entries.Clear();

            while (_pool.Count > 0)
            {
                var root = _pool.Pop();
                if (root != null) Object.Destroy(root);
            }
        }

        private GameObject Rent()
        {
            if (_pool.Count > 0)
            {
                var go = _pool.Pop();
                go.SetActive(true);
                return go;
            }
            return CreateTemplate();
        }

        private void Return(DisplayEntry entry)
        {
            entry.NameText.text = "";
            entry.ClassIcon.sprite = null;
            entry.ClassIcon.enabled = false;
            entry.Root.SetActive(false);
            _pool.Push(entry.Root);
        }

        private GameObject CreateTemplate()
        {
            var root = new GameObject("Nameplate");
            root.transform.SetParent(_canvasTransform, false);

            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;

            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var iconGo = new GameObject("ClassIcon");
            iconGo.transform.SetParent(root.transform, false);
            var icon = iconGo.AddComponent<Image>();
            icon.rectTransform.sizeDelta = _settings.IconSize;
            icon.enabled = false;

            var textGo = new GameObject("NameText");
            textGo.transform.SetParent(root.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            if (_settings.Font != null) text.font = _settings.Font;
            if (_settings.FontMaterial != null) text.fontMaterial = _settings.FontMaterial;
            text.fontSize = _settings.FontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.outlineWidth = _settings.OutlineWidth;
            text.outlineColor = _settings.OutlineColor;

            return root;
        }
    }
}
