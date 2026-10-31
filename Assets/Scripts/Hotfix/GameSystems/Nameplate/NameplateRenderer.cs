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
        private readonly Stack<GameObject> _healthPool = new();
        private readonly Dictionary<int, DisplayEntry> _entries = new();
        private Sprite _whiteSprite;
        private Texture2D _whiteTexture;

        private struct DisplayEntry
        {
            public Transform Owner;
            public GameObject Root;
            public TMP_Text NameText;
            public Image ClassIcon;
            public NameplateConfig Config;
            public Image HealthFill;
            public bool HasHealthBar;
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
            nameText.color = config.NameColor ?? _settings.DefaultColor;

            // Apply per-config font settings if specified
            if (config.Font != null)
            {
                nameText.font = config.Font;
                if(config.FontMaterial != null) nameText.fontMaterial = config.FontMaterial;
                nameText.fontSize = config.FontSize > 0 ? config.FontSize : _settings.FontSize;
            }
            else
            {
                nameText.fontSize = _settings.FontSize;
            }

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

        /// <summary>
        /// 注册带血条的名牌（远程玩家用）。独立模板 + 独立对象池，与单机的
        /// <see cref="Register"/> 互不影响。重复 id 与旧路径同款守卫。
        /// </summary>
        public void RegisterWithHealth(int entityId, Transform owner, NameplateConfig config)
        {
            if (_entries.ContainsKey(entityId)) return;

            var root = Rent(withHealth: true);
            var nameText = root.transform.Find("NameRow/NameText").GetComponent<TextMeshProUGUI>();
            var classIcon = root.transform.Find("NameRow/ClassIcon").GetComponent<Image>();
            var healthFill = root.transform.Find("HPBar/Fill").GetComponent<Image>();

            nameText.text = config.DisplayName;
            nameText.color = config.NameColor ?? _settings.DefaultColor;

            if (config.Font != null)
            {
                nameText.font = config.Font;
                if (config.FontMaterial != null) nameText.fontMaterial = config.FontMaterial;
                nameText.fontSize = config.FontSize > 0 ? config.FontSize : _settings.FontSize;
            }
            else
            {
                nameText.fontSize = _settings.FontSize;
            }

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
                Config = config,
                HealthFill = healthFill,
                HasHealthBar = true
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

        /// <summary>更新血条填充量（0..1）。非带血条条目（单机名牌/未注册）静默忽略。</summary>
        public void UpdateHealth(int entityId, float percent)
        {
            if (_entries.TryGetValue(entityId, out var entry) && entry.HealthFill != null)
                entry.HealthFill.fillAmount = Mathf.Clamp01(percent);
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

                var worldPos = entry.Owner.position + Vector3.up * (entry.Config.VerticalOffset >= 0 ? entry.Config.VerticalOffset : _settings.VerticalOffset);
                var screenPos = camera.WorldToScreenPoint(worldPos);
                // 锚点在摄像机后方时隐藏，否则位置不更新会停留在旧值（如画布中心）导致名片钉在屏幕上
                if (screenPos.z <= 0)
                {
                    entry.Root.SetActive(false);
                    continue;
                }
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
                if (kv.Value.Root != null) DestroyObject(kv.Value.Root);
            _entries.Clear();

            while (_pool.Count > 0)
            {
                var root = _pool.Pop();
                if (root != null) DestroyObject(root);
            }

            while (_healthPool.Count > 0)
            {
                var root = _healthPool.Pop();
                if (root != null) DestroyObject(root);
            }

            if (_whiteSprite != null) DestroyObject(_whiteSprite);
            _whiteSprite = null;
            if (_whiteTexture != null) DestroyObject(_whiteTexture);
            _whiteTexture = null;
        }

        private GameObject Rent(bool withHealth = false)
        {
            var pool = withHealth ? _healthPool : _pool;
            if (pool.Count > 0)
            {
                var go = pool.Pop();
                go.SetActive(true);
                return go;
            }
            return withHealth ? CreateHealthTemplate() : CreateTemplate();
        }

        private void Return(DisplayEntry entry)
        {
            entry.NameText.text = "";
            entry.ClassIcon.sprite = null;
            entry.ClassIcon.enabled = false;
            entry.Root.SetActive(false);

            if (entry.HasHealthBar)
            {
                if (entry.HealthFill != null)
                {
                    entry.HealthFill.fillAmount = 1f;
                    entry.HealthFill.color = Color.white;
                }
                _healthPool.Push(entry.Root);
                return;
            }

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

        /// <summary>
        /// 带血条模板：纵向 Root（名字行 + HPBar），pivot (0.5,1) 让人物头顶锚点落在
        /// 整块下沿之上。与单机模板完全独立——不共用对象池，单机名牌行为不变。
        /// </summary>
        private GameObject CreateHealthTemplate()
        {
            var root = new GameObject("NameplateHealth");
            root.transform.SetParent(_canvasTransform, false);

            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 2f;

            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.pivot = new Vector2(0.5f, 1f);

            var nameRow = new GameObject("NameRow");
            nameRow.transform.SetParent(root.transform, false);

            var rowLayout = nameRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = 4f;

            var iconGo = new GameObject("ClassIcon");
            iconGo.transform.SetParent(nameRow.transform, false);
            var icon = iconGo.AddComponent<Image>();
            icon.rectTransform.sizeDelta = _settings.IconSize;
            icon.enabled = false;

            var textGo = new GameObject("NameText");
            textGo.transform.SetParent(nameRow.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            if (_settings.Font != null) text.font = _settings.Font;
            if (_settings.FontMaterial != null) text.fontMaterial = _settings.FontMaterial;
            text.fontSize = _settings.FontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.outlineWidth = _settings.OutlineWidth;
            text.outlineColor = _settings.OutlineColor;

            var barGo = new GameObject("HPBar");
            barGo.transform.SetParent(root.transform, false);
            var background = barGo.AddComponent<Image>();
            background.sprite = EnsureWhiteSprite();
            background.color = new Color(0f, 0f, 0f, 0.6f);
            background.raycastTarget = false;
            var barRect = barGo.GetComponent<RectTransform>();
            barRect.sizeDelta = new Vector2(80f, 6f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(barGo.transform, false);
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = EnsureWhiteSprite();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            fill.color = new Color(0.85f, 0.2f, 0.2f, 1f);
            fill.raycastTarget = false;
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            return root;
        }

        /// <summary>1×1 白色 sprite（懒创建；Fill 的 Image.Type.Filled 需要一张 sprite）</summary>
        private Sprite EnsureWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;

            _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTexture.name = "NameplateHealthWhite";
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();

            _whiteSprite = Sprite.Create(_whiteTexture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "NameplateHealthWhite";
            return _whiteSprite;
        }

        /// <summary>EditMode 测试也会调 Cleanup——EditMode 下 Object.Destroy 会报错，需 DestroyImmediate</summary>
        private static void DestroyObject(Object obj)
        {
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
