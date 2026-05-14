using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Nameplate
{
    public class EntityDisplayManager : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _fontAsset;
        [SerializeField] private Material _fontMaterial;
        [SerializeField] private float _cullDistance = 50f;
        [SerializeField] private float _fadeStartDistance = 30f;

        private Canvas _canvas;
        private Camera _camera;

        private readonly Stack<GameObject> _nameplateFree = new();
        private readonly Dictionary<int, DisplayEntry> _entries = new();

        private readonly Stack<TextMeshProUGUI> _floatTextFree = new();
        private readonly HashSet<TextMeshProUGUI> _floatTextActive = new();

        private readonly Dictionary<long, MergeEntry> _mergeTracker = new();

        private Tween _shakeTween;
        private float _lastShakeTime = -1f;
        private const float ShakeCooldown = 0.1f;
        private const float MergeWindow = 0.2f;

        private struct DisplayEntry
        {
            public Transform Owner;
            public GameObject Root;
            public TMP_Text NameText;
            public Image ClassIcon;
            public NameplateConfig Config;
        }

        private class MergeEntry
        {
            public int Count;
            public int Sum;
            public float LastHitTime;
            public TextMeshProUGUI Tmp;
        }

        public static EntityDisplayManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _camera = Camera.main;
            CreateCanvas();
        }

        private void CreateCanvas()
        {
            var go = new GameObject("EntityDisplayCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 4500;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Subscribe<SkillActivatedEvent>(OnSkillActivated);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Unsubscribe<SkillActivatedEvent>(OnSkillActivated);
        }

        private void LateUpdate()
        {
            if (_camera == null) { _camera = Camera.main; if (_camera == null) return; }

            var camPos = _camera.transform.position;
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
                var cullEnd = entry.Config.CullDistance > 0 ? entry.Config.CullDistance : _cullDistance;

                if (dist > cullEnd)
                {
                    entry.Root.SetActive(false);
                    continue;
                }

                entry.Root.SetActive(true);

                var worldPos = entry.Owner.position + Vector3.up * entry.Config.VerticalOffset;
                var screenPos = _camera.WorldToScreenPoint(worldPos);
                if (screenPos.z > 0)
                    entry.Root.transform.position = screenPos;

                float alpha = dist > _fadeStartDistance
                    ? 1f - Mathf.Clamp01((dist - _fadeStartDistance) / (cullEnd - _fadeStartDistance))
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
                    ReturnNameplate(entry);
                _entries.Remove(id);
            }

            var expiredKeys = new List<long>();
            foreach (var kv in _mergeTracker)
                if (Time.time - kv.Value.LastHitTime > MergeWindow)
                    expiredKeys.Add(kv.Key);
            foreach (var k in expiredKeys)
                _mergeTracker.Remove(k);
        }

        private void OnDestroy()
        {
            _shakeTween?.Kill();
            _mergeTracker.Clear();
            foreach (var tmp in _floatTextActive)
                if (tmp != null) Destroy(tmp.gameObject);
            _floatTextActive.Clear();

            foreach (var tmp in _floatTextFree)
                if (tmp != null) Destroy(tmp.gameObject);
            _floatTextFree.Clear();

            foreach (var kv in _entries)
                if (kv.Value.Root != null) Destroy(kv.Value.Root);
            _entries.Clear();

            foreach (var root in _nameplateFree)
                if (root != null) Destroy(root);
            _nameplateFree.Clear();
        }

        // ===== Nameplate API =====

        public void Register(int entityId, Transform owner, NameplateConfig config)
        {
            if (_entries.ContainsKey(entityId)) return;

            var root = RentNameplateRoot();
            var nameText = root.GetComponentInChildren<TextMeshProUGUI>();
            var classIcon = root.GetComponentInChildren<Image>();

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
            ReturnNameplate(entry);
            _entries.Remove(entityId);
        }

        public void UpdateName(int entityId, string newName)
        {
            if (_entries.TryGetValue(entityId, out var entry))
                entry.NameText.text = newName;
        }

        public void SetNameplateVisible(int entityId, bool visible)
        {
            if (_entries.TryGetValue(entityId, out var entry))
                entry.Root.SetActive(visible);
        }

        // ===== Float Text API =====

        public void ShowFloatingText(Vector3 worldPos, FloatTextConfig config)
        {
            SpawnFloatText(worldPos, config, 0);
        }

        public void ShowDamageText(int entityId, Vector3 worldPos, FloatTextConfig config, int value)
        {
            var mergeKey = MakeMergeKey(entityId, config.Type);

            if (_mergeTracker.TryGetValue(mergeKey, out var merge)
                && Time.time - merge.LastHitTime < MergeWindow)
            {
                merge.Count++;
                merge.Sum += value;
                merge.LastHitTime = Time.time;
                merge.Tmp.text = $"-{merge.Sum}";
                merge.Tmp.alpha = 1f;
                return;
            }

            var tmp = SpawnFloatText(worldPos, config, value);

            if (config.Type == FloatTextType.Normal || config.Type == FloatTextType.Crit)
            {
                _mergeTracker[mergeKey] = new MergeEntry
                {
                    Count = 1,
                    Sum = value,
                    LastHitTime = Time.time,
                    Tmp = tmp
                };
            }

            if (config.Type == FloatTextType.Crit && _camera != null
                && Time.time - _lastShakeTime > ShakeCooldown)
            {
                _lastShakeTime = Time.time;
                _shakeTween?.Kill();
                _shakeTween = _camera.transform.DOPunchPosition(new Vector3(2f, 1f, 0f), 0.15f, 5, 0.5f);
            }
        }

        // ===== Internal =====

        private static long MakeMergeKey(int entityId, FloatTextType type)
        {
            return ((long)entityId << 32) | (long)type;
        }

        private GameObject RentNameplateRoot()
        {
            if (_nameplateFree.Count > 0)
            {
                var go = _nameplateFree.Pop();
                go.SetActive(true);
                return go;
            }
            return CreateNameplateTemplate();
        }

        private void ReturnNameplate(DisplayEntry entry)
        {
            entry.NameText.text = "";
            entry.ClassIcon.sprite = null;
            entry.ClassIcon.enabled = false;
            entry.Root.SetActive(false);
            _nameplateFree.Push(entry.Root);
        }

        private GameObject CreateNameplateTemplate()
        {
            var root = new GameObject("Nameplate");
            root.transform.SetParent(_canvas.transform, false);

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
            icon.rectTransform.sizeDelta = new Vector2(20, 20);
            icon.enabled = false;

            var textGo = new GameObject("NameText");
            textGo.transform.SetParent(root.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            if (_fontAsset != null) text.font = _fontAsset;
            if (_fontMaterial != null) text.fontMaterial = _fontMaterial;
            text.fontSize = 18;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.outlineWidth = 0.15f;
            text.outlineColor = Color.black;

            return root;
        }

        private TextMeshProUGUI RentFloatTMP()
        {
            if (_floatTextFree.Count > 0)
            {
                var tmp = _floatTextFree.Pop();
                tmp.gameObject.SetActive(true);
                return tmp;
            }
            return CreateFloatTMP(active: true);
        }

        private TextMeshProUGUI CreateFloatTMP(bool active = false)
        {
            var go = new GameObject("FloatText");
            go.transform.SetParent(_canvas.transform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (_fontAsset != null) tmp.font = _fontAsset;
            if (_fontMaterial != null) tmp.fontMaterial = _fontMaterial;
            tmp.raycastTarget = false;
            tmp.alignment = TextAlignmentOptions.Center;
            go.SetActive(active);
            return tmp;
        }

        private void ReturnFloatText(TextMeshProUGUI tmp)
        {
            _floatTextActive.Remove(tmp);
            tmp.text = "";
            tmp.alpha = 1f;
            tmp.rectTransform.localScale = Vector3.one;
            tmp.gameObject.SetActive(false);
            _floatTextFree.Push(tmp);
        }

        private TextMeshProUGUI SpawnFloatText(Vector3 worldPos, FloatTextConfig config, int value)
        {
            var tmp = RentFloatTMP();
            _floatTextActive.Add(tmp);

            if (!string.IsNullOrEmpty(config.TextOverride))
            {
                tmp.text = config.TextOverride;
            }
            else
            {
                tmp.text = config.Type == FloatTextType.Heal ? $"+{value}" : $"-{value}";
            }

            tmp.color = config.Color;
            tmp.fontSize = config.FontSize;
            tmp.alpha = 1f;

            if (_camera != null)
            {
                var screenPos = _camera.WorldToScreenPoint(worldPos);
                tmp.rectTransform.position = screenPos;
            }

            var rt = tmp.rectTransform;
            var startY = rt.anchoredPosition.y;
            var seq = DOTween.Sequence();

            switch (config.Type)
            {
                case FloatTextType.Crit:
                    rt.localScale = Vector3.one * 0.6f;
                    seq.Append(rt.DOScale(1.3f, 0.15f).SetEase(Ease.OutBack));
                    seq.Join(rt.DOAnchorPosY(startY + config.MoveUpDistance, config.Duration)
                        .SetEase(Ease.OutCubic));
                    seq.Join(tmp.DOFade(0f, config.Duration * 0.4f)
                        .SetDelay(config.Duration * 0.6f));
                    break;

                case FloatTextType.Dodge:
                case FloatTextType.Block:
                    seq.Join(rt.DOAnchorPos(new Vector2(
                        rt.anchoredPosition.x + Random.Range(20f, 40f),
                        startY + config.MoveUpDistance), config.Duration)
                        .SetEase(Ease.OutCubic));
                    seq.Join(tmp.DOFade(0f, config.Duration * 0.5f)
                        .SetDelay(config.Duration * 0.5f));
                    break;

                case FloatTextType.SkillName:
                    rt.localScale = Vector3.one * 0.5f;
                    seq.Append(rt.DOScale(1.2f, 0.2f).SetEase(Ease.OutBack));
                    seq.Append(rt.DOScale(0.8f, config.Duration - 0.2f));
                    seq.Join(tmp.DOFade(0f, config.Duration * 0.4f)
                        .SetDelay(config.Duration * 0.6f));
                    break;

                default:
                    rt.localScale = Vector3.one;
                    seq.Join(rt.DOAnchorPosY(startY + config.MoveUpDistance, config.Duration)
                        .SetEase(Ease.OutCubic));
                    seq.Join(tmp.DOFade(0f, config.Duration * 0.5f)
                        .SetDelay(config.Duration * 0.5f));
                    break;
            }

            seq.OnKill(() => ReturnFloatText(tmp));
            seq.SetTarget(tmp.transform);

            return tmp;
        }

        // ===== Event Handlers =====

        private void OnPlayerDamaged(DamageEvent e)
        {
            var preset = e.IsCritical ? FloatTextPresets.CritDamage : FloatTextPresets.Damage;
            ShowDamageText(e.TargetId, Vector3.up * 2f, preset, Mathf.CeilToInt(e.Damage));
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var preset = e.IsCritical ? FloatTextPresets.CritDamage : FloatTextPresets.Damage;
            ShowDamageText(e.EntityId, e.HitPosition, preset, e.Damage);
        }

        private void OnSkillActivated(SkillActivatedEvent e)
        {
            ShowFloatingText(Vector3.zero, FloatTextPresets.SkillName(e.SkillName));
        }
    }
}
