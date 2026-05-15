# EntityDisplayManager Refactor + Configuration System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split EntityDisplayManager into 4 focused subsystems and externalize all visual parameters to ScriptableObject assets for runtime Inspector debugging.

**Architecture:** EntityDisplayManager becomes a thin coordinator (~60 lines) that creates the ScreenSpaceOverlay Canvas, holds 8 ScriptableObject settings references, and instantiates 4 internal subsystem objects. Each subsystem owns its pool, its update logic, and reads visual params from settings. DisplayEventBridge subscribes to EventBus and maps damage/skill events to FloatTextRenderer + DamageScreenEffect calls.

**Tech Stack:** Unity 2022.3, TextMeshPro, DOTween, existing EventBus (Sys3C.Core.Events)

---

## File Structure

| Action | File | Purpose |
|--------|------|---------|
| Create | `FloatTextSettings.cs` | ScriptableObject for float text visual params |
| Create | `NameplateSettings.cs` | ScriptableObject for nameplate visual params |
| Create | `NameplateRenderer.cs` | Nameplate pool + Register/Unregister + per-frame positioning |
| Create | `FloatTextRenderer.cs` | Float text pool + Spawn/DOTween + MergeTracker |
| Create | `DamageScreenEffect.cs` | Fullscreen red flash on non-NPC damage (3s cooldown) |
| Create | `DisplayEventBridge.cs` | EventBus subscription bridge |
| Rewrite | `EntityDisplayManager.cs` | Thin coordinator (~60 lines) |
| Modify | `NameplateConfig.cs` | Make NameColor nullable, remove VerticalOffset/CullDistance |
| Modify | `FloatTextConfig.cs` | Remove visual params + FloatTextPresets, keep enum + FloatTextConfig |
| Modify | `MonsterEntity.cs` | Remove ColorPalette dependency |
| Delete | `ColorPalette.cs` | Colors migrated to Settings assets |
| Create | 8 `.asset` files in `Assets/Settings/Display/` | Runtime-configurable settings instances |

---

### Task 1: Create FloatTextSettings.cs (ScriptableObject)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextSettings.cs`

No dependencies. `FloatTextType` enum stays in `FloatTextConfig.cs` (already exists).

- [ ] **Step 1: Write the ScriptableObject class**

```csharp
using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    [CreateAssetMenu(menuName = "Display/FloatTextSettings", fileName = "FloatTextSettings")]
    public class FloatTextSettings : ScriptableObject
    {
        public FloatTextType Type;
        public TMP_FontAsset Font;
        public Material FontMaterial;
        public float FontSize = 36f;
        public Color Color = Color.white;
        public float Duration = 1f;
        public float MoveUpDistance = 50f;
        [Range(0f, 1f)] public float FadeStartRatio = 0.5f;
        public float StartScale = 1f;
    }
}
```

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextSettings.cs*
git commit -m "feat: add FloatTextSettings ScriptableObject"
```

---

### Task 2: Create NameplateSettings.cs (ScriptableObject)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateSettings.cs`

- [ ] **Step 1: Write the ScriptableObject class**

```csharp
using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    [CreateAssetMenu(menuName = "Display/NameplateSettings", fileName = "NameplateSettings")]
    public class NameplateSettings : ScriptableObject
    {
        public TMP_FontAsset Font;
        public Material FontMaterial;
        public float FontSize = 18f;
        public Color DefaultColor = Color.white;
        public float OutlineWidth = 0.15f;
        public Color OutlineColor = Color.black;
        public float VerticalOffset = 2.5f;
        public float CullDistance = 50f;
        public float FadeStartDistance = 30f;
        public Vector2 IconSize = new(20, 20);
    }
}
```

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateSettings.cs*
git commit -m "feat: add NameplateSettings ScriptableObject"
```

---

### Task 3: Create NameplateRenderer.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateRenderer.cs`

Extracts nameplate pool + Register/Unregister + per-frame WorldToScreenPos + distance culling + alpha fade from EntityDisplayManager.

- [ ] **Step 1: Write NameplateRenderer**

```csharp
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
            nameText.color = config.NameColor ?? _settings.DefaultColor;

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
```

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateRenderer.cs*
git commit -m "feat: extract NameplateRenderer from EntityDisplayManager"
```

---

### Task 4: Create FloatTextRenderer.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextRenderer.cs`

Extracts float text pool + Spawn + DOTween animation sequencing + MergeTracker from EntityDisplayManager.

- [ ] **Step 1: Write FloatTextRenderer**

```csharp
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class FloatTextRenderer
    {
        private readonly Transform _canvasTransform;
        private readonly Stack<TextMeshProUGUI> _pool = new();
        private readonly HashSet<TextMeshProUGUI> _active = new();
        private readonly Dictionary<long, MergeEntry> _mergeTracker = new();
        private const float MergeWindow = 0.2f;

        public Camera Camera { get; set; }

        private class MergeEntry
        {
            public int Count;
            public int Sum;
            public float LastHitTime;
            public TextMeshProUGUI Tmp;
        }

        public FloatTextRenderer(Transform canvasTransform)
        {
            _canvasTransform = canvasTransform;
        }

        public void ShowFloatingText(Vector3 worldPos, FloatTextSettings settings, string text)
        {
            Spawn(worldPos, settings, 0, text);
        }

        public void ShowDamageText(int entityId, Vector3 worldPos, FloatTextSettings settings, int value)
        {
            var mergeKey = MakeMergeKey(entityId, settings.Type);

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

            var tmp = Spawn(worldPos, settings, value, null);

            if (settings.Type == FloatTextType.Normal || settings.Type == FloatTextType.Crit)
            {
                _mergeTracker[mergeKey] = new MergeEntry
                {
                    Count = 1,
                    Sum = value,
                    LastHitTime = Time.time,
                    Tmp = tmp
                };
            }
        }

        public void PurgeExpiredMerges()
        {
            var expired = new List<long>();
            foreach (var kv in _mergeTracker)
                if (Time.time - kv.Value.LastHitTime > MergeWindow)
                    expired.Add(kv.Key);
            foreach (var k in expired)
                _mergeTracker.Remove(k);
        }

        public void Cleanup()
        {
            foreach (var tmp in _active)
                if (tmp != null) Object.Destroy(tmp.gameObject);
            _active.Clear();

            while (_pool.Count > 0)
            {
                var tmp = _pool.Pop();
                if (tmp != null) Object.Destroy(tmp.gameObject);
            }
        }

        private TextMeshProUGUI Spawn(Vector3 worldPos, FloatTextSettings settings, int value, string textOverride)
        {
            var tmp = Rent();
            _active.Add(tmp);

            if (!string.IsNullOrEmpty(textOverride))
                tmp.text = textOverride;
            else
                tmp.text = settings.Type == FloatTextType.Heal ? $"+{value}" : $"-{value}";

            tmp.color = settings.Color;
            tmp.fontSize = settings.FontSize;
            if (settings.Font != null) tmp.font = settings.Font;
            if (settings.FontMaterial != null) tmp.fontMaterial = settings.FontMaterial;
            tmp.alpha = 1f;

            if (Camera != null)
            {
                var screenPos = Camera.WorldToScreenPoint(worldPos);
                tmp.rectTransform.position = screenPos;
            }

            var rt = tmp.rectTransform;
            var startY = rt.anchoredPosition.y;
            var seq = DOTween.Sequence();

            switch (settings.Type)
            {
                case FloatTextType.Crit:
                    rt.localScale = Vector3.one * 0.6f;
                    seq.Append(rt.DOScale(1.3f, 0.15f).SetEase(Ease.OutBack));
                    seq.Join(rt.DOAnchorPosY(startY + settings.MoveUpDistance, settings.Duration)
                        .SetEase(Ease.OutCubic));
                    seq.Join(tmp.DOFade(0f, settings.Duration * (1f - settings.FadeStartRatio))
                        .SetDelay(settings.Duration * settings.FadeStartRatio));
                    break;

                case FloatTextType.Dodge:
                case FloatTextType.Block:
                    seq.Join(rt.DOAnchorPos(new Vector2(
                        rt.anchoredPosition.x + Random.Range(20f, 40f),
                        startY + settings.MoveUpDistance), settings.Duration)
                        .SetEase(Ease.OutCubic));
                    seq.Join(tmp.DOFade(0f, settings.Duration * (1f - settings.FadeStartRatio))
                        .SetDelay(settings.Duration * settings.FadeStartRatio));
                    break;

                case FloatTextType.SkillName:
                    rt.localScale = Vector3.one * 0.5f;
                    seq.Append(rt.DOScale(1.2f, 0.2f).SetEase(Ease.OutBack));
                    seq.Append(rt.DOScale(0.8f, settings.Duration - 0.2f));
                    seq.Join(tmp.DOFade(0f, settings.Duration * (1f - settings.FadeStartRatio))
                        .SetDelay(settings.Duration * settings.FadeStartRatio));
                    break;

                default: // Normal, Heal, DOT
                    rt.localScale = Vector3.one * settings.StartScale;
                    seq.Join(rt.DOAnchorPosY(startY + settings.MoveUpDistance, settings.Duration)
                        .SetEase(Ease.OutCubic));
                    seq.Join(tmp.DOFade(0f, settings.Duration * (1f - settings.FadeStartRatio))
                        .SetDelay(settings.Duration * settings.FadeStartRatio));
                    break;
            }

            seq.OnKill(() =>
            {
                _active.Remove(tmp);
                tmp.text = "";
                tmp.alpha = 1f;
                tmp.rectTransform.localScale = Vector3.one;
                tmp.gameObject.SetActive(false);
                _pool.Push(tmp);
            });
            seq.SetTarget(tmp.transform);

            return tmp;
        }

        private TextMeshProUGUI Rent()
        {
            if (_pool.Count > 0)
            {
                var tmp = _pool.Pop();
                tmp.gameObject.SetActive(true);
                return tmp;
            }
            return CreateTMP(active: true);
        }

        private TextMeshProUGUI CreateTMP(bool active)
        {
            var go = new GameObject("FloatText");
            go.transform.SetParent(_canvasTransform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.raycastTarget = false;
            tmp.alignment = TextAlignmentOptions.Center;
            go.SetActive(active);
            return tmp;
        }

        private static long MakeMergeKey(int entityId, FloatTextType type)
        {
            return ((long)entityId << 32) | (long)type;
        }
    }
}
```

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextRenderer.cs*
git commit -m "feat: extract FloatTextRenderer from EntityDisplayManager"
```

---

### Task 5: Create DamageScreenEffect.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/DamageScreenEffect.cs`

Fullscreen red flash effect. Only triggers on non-NPC damage (player taking damage), 3-second cooldown.

- [ ] **Step 1: Write DamageScreenEffect**

```csharp
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.Nameplate
{
    public class DamageScreenEffect
    {
        private readonly Image _overlay;
        private float _lastFlashTime = -999f;
        private const float FlashCooldown = 3f;

        public DamageScreenEffect(Transform canvasTransform)
        {
            var go = new GameObject("DamageOverlay");
            go.transform.SetParent(canvasTransform, false);
            _overlay = go.AddComponent<Image>();
            _overlay.color = new Color(1f, 0f, 0f, 0f);
            _overlay.raycastTarget = false;

            var rt = _overlay.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public void Flash()
        {
            if (Time.time - _lastFlashTime < FlashCooldown) return;
            _lastFlashTime = Time.time;

            _overlay.DOKill();
            _overlay.DOFade(0.15f, 0.1f).OnComplete(() =>
            {
                _overlay.DOFade(0f, 2.5f);
            });
        }

        public void Cleanup()
        {
            _overlay.DOKill();
            if (_overlay != null && _overlay.gameObject != null)
                Object.Destroy(_overlay.gameObject);
        }
    }
}
```

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/DamageScreenEffect.cs*
git commit -m "feat: add DamageScreenEffect (red flash on player damage)"
```

---

### Task 6: Create DisplayEventBridge.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs`

Bridges EventBus (DamageEvent, MonsterTakeDamageEvent, SkillActivatedEvent) to FloatTextRenderer and DamageScreenEffect.

- [ ] **Step 1: Write DisplayEventBridge**

```csharp
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class DisplayEventBridge
    {
        private readonly FloatTextRenderer _floatText;
        private readonly DamageScreenEffect _damageScreenEffect;
        private readonly FloatTextSettings _damageSettings;
        private readonly FloatTextSettings _critDamageSettings;
        private readonly FloatTextSettings _skillNameSettings;

        public DisplayEventBridge(
            FloatTextRenderer floatText,
            DamageScreenEffect damageScreenEffect,
            FloatTextSettings damageSettings,
            FloatTextSettings critDamageSettings,
            FloatTextSettings skillNameSettings)
        {
            _floatText = floatText;
            _damageScreenEffect = damageScreenEffect;
            _damageSettings = damageSettings;
            _critDamageSettings = critDamageSettings;
            _skillNameSettings = skillNameSettings;
        }

        public void Enable()
        {
            EventBus.Subscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Subscribe<SkillActivatedEvent>(OnSkillActivated);
        }

        public void Disable()
        {
            EventBus.Unsubscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            EventBus.Unsubscribe<SkillActivatedEvent>(OnSkillActivated);
        }

        private void OnPlayerDamaged(DamageEvent e)
        {
            _damageScreenEffect.Flash();
            var settings = e.IsCritical ? _critDamageSettings : _damageSettings;
            _floatText.ShowDamageText(e.TargetId, Vector3.up * 2f, settings, Mathf.CeilToInt(e.Damage));
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var settings = e.IsCritical ? _critDamageSettings : _damageSettings;
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, settings, e.Damage);
        }

        private void OnSkillActivated(SkillActivatedEvent e)
        {
            _floatText.ShowFloatingText(Vector3.zero, _skillNameSettings, e.SkillName);
        }
    }
}
```

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs*
git commit -m "feat: extract DisplayEventBridge from EntityDisplayManager"
```

---

### Task 7: Rewrite EntityDisplayManager.cs

**Files:**
- Rewrite: `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs`

~460 lines → ~100 lines. Thin coordinator: creates Canvas, instantiates subsystems, holds settings references, forwards API calls.

- [ ] **Step 1: Write the new EntityDisplayManager**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.Nameplate
{
    public class EntityDisplayManager : MonoBehaviour
    {
        [SerializeField] private NameplateSettings _nameplateSettings;
        [SerializeField] private FloatTextSettings _damageSettings;
        [SerializeField] private FloatTextSettings _critDamageSettings;
        [SerializeField] private FloatTextSettings _skillNameSettings;

        private Canvas _canvas;
        private Camera _camera;
        private NameplateRenderer _nameplate;
        private FloatTextRenderer _floatText;
        private DisplayEventBridge _eventBridge;
        private DamageScreenEffect _damageScreenEffect;

        public static EntityDisplayManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _camera = Camera.main;

            CreateCanvas();

            _nameplate = new NameplateRenderer(_nameplateSettings, _canvas.transform);
            _floatText = new FloatTextRenderer(_canvas.transform);
            _damageScreenEffect = new DamageScreenEffect(_canvas.transform);

            _eventBridge = new DisplayEventBridge(
                _floatText, _damageScreenEffect,
                _damageSettings, _critDamageSettings, _skillNameSettings);
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
            _eventBridge?.Enable();
        }

        private void OnDisable()
        {
            _eventBridge?.Disable();
        }

        private void LateUpdate()
        {
            if (_camera == null) { _camera = Camera.main; if (_camera == null) return; }
            _floatText.Camera = _camera;
            _nameplate.Tick(_camera);
            _floatText.PurgeExpiredMerges();
        }

        private void OnDestroy()
        {
            _nameplate?.Cleanup();
            _floatText?.Cleanup();
            _damageScreenEffect?.Cleanup();
        }

        // ===== Nameplate API =====

        public void Register(int entityId, Transform owner, NameplateConfig config)
        {
            _nameplate.Register(entityId, owner, config);
        }

        public void Unregister(int entityId)
        {
            _nameplate.Unregister(entityId);
        }

        public void UpdateName(int entityId, string newName)
        {
            _nameplate.UpdateName(entityId, newName);
        }

        public void SetNameplateVisible(int entityId, bool visible)
        {
            _nameplate.SetVisible(entityId, visible);
        }

        // ===== Float Text API =====

        public void ShowFloatingText(Vector3 worldPos, FloatTextSettings settings, string text)
        {
            _floatText.ShowFloatingText(worldPos, settings, text);
        }

        public void ShowDamageText(int entityId, Vector3 worldPos, FloatTextSettings settings, int value)
        {
            _floatText.ShowDamageText(entityId, worldPos, settings, value);
        }
    }
}
```

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors — `FloatTextPresets` and `ColorPalette` references are all in the old code being replaced, but those classes still exist until later tasks delete them. Check for unused-import warnings only.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs
git commit -m "refactor: rewrite EntityDisplayManager as thin coordinator"
```

---

### Task 8: Update NameplateConfig.cs

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateConfig.cs`

Make `NameColor` nullable (null = use `NameplateSettings.DefaultColor`), remove `VerticalOffset` and `CullDistance`.

- [ ] **Step 1: Rewrite NameplateConfig**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public struct NameplateConfig
    {
        public string DisplayName;
        public Color? NameColor;
        public Sprite ClassIcon;

        public NameplateConfig(string displayName, Color? color = null)
        {
            DisplayName = displayName;
            NameColor = color;
            ClassIcon = null;
        }
    }
}
```

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateConfig.cs
git commit -m "refactor: simplify NameplateConfig (nullable color, remove offset/distance)"
```

---

### Task 9: Update FloatTextConfig.cs

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextConfig.cs`

Remove `FloatTextPresets` static class and all preset definitions. Remove visual params from `FloatTextConfig`. Keep `FloatTextType` enum and the `FloatTextConfig` class (simplified).

- [ ] **Step 1: Rewrite FloatTextConfig**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public enum FloatTextType
    {
        Normal,
        Crit,
        Heal,
        Dodge,
        Block,
        DOT,
        SkillName
    }

    public class FloatTextConfig
    {
        public FloatTextSettings Settings;
        public string TextOverride;
        public bool ShowName;
    }
}
```

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no errors — `FloatTextPresets` was only referenced in old `EntityDisplayManager.cs` (now rewritten).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextConfig.cs
git commit -m "refactor: remove FloatTextPresets, simplify FloatTextConfig to settings ref"
```

---

### Task 10: Update MonsterEntity.cs

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

Remove `ColorPalette.Monster` usage. With nullable `NameColor`, passing no color uses `NameplateSettings.DefaultColor`.

- [ ] **Step 1: Edit MonsterEntity.cs Init()**

Replace line 84:
```csharp
var cfg = new NameplateConfig(_config.DisplayName, ColorPalette.Monster);
```
with:
```csharp
var cfg = new NameplateConfig(_config.DisplayName);
```

Use `script-read` to get the file, then `script-update-or-create` with the change, or use the `Edit` MCP tool equivalent. The only line change is removing `, ColorPalette.Monster` from the constructor call.

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "refactor: remove ColorPalette dependency from MonsterEntity"
```

---

### Task 11: Delete ColorPalette.cs

**Files:**
- Delete: `Assets/Scripts/Hotfix/GameSystems/Nameplate/ColorPalette.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Nameplate/ColorPalette.cs.meta`

No remaining references exist after Task 10.

- [ ] **Step 1: Delete the files**

Use `assets-delete` with paths `Assets/Scripts/Hotfix/GameSystems/Nameplate/ColorPalette.cs` and its `.meta`.

- [ ] **Step 2: Refresh assets and verify compilation**

Run `assets-refresh`. Then `console-get-logs` filter Warning/Error. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/ColorPalette.cs*
git commit -m "chore: delete ColorPalette (colors migrated to Settings assets)"
```

---

### Task 12: Create ScriptableObject .asset files

**Files:**
- Create: `Assets/Settings/Display/NameplateSettings.asset`
- Create: `Assets/Settings/Display/FloatText_Damage.asset`
- Create: `Assets/Settings/Display/FloatText_CritDamage.asset`
- Create: `Assets/Settings/Display/FloatText_Heal.asset`
- Create: `Assets/Settings/Display/FloatText_Dodge.asset`
- Create: `Assets/Settings/Display/FloatText_Block.asset`
- Create: `Assets/Settings/Display/FloatText_DOT.asset`
- Create: `Assets/Settings/Display/FloatText_SkillName.asset`

Use `script-execute` to create assets via `AssetDatabase.CreateAsset`. Must be done in Unity Editor.

- [ ] **Step 1: Create the Settings/Display directory**

Use `assets-create-folder` with parent `Assets/Settings` and name `Display`. If `Assets/Settings` doesn't exist, create it first.

- [ ] **Step 2: Create all 8 .asset files via script-execute**

Run the following C# via `script-execute` (isMethodBody=true):

```csharp
var nameplateSettings = ScriptableObject.CreateInstance<NameplateSettings>();
nameplateSettings.FontSize = 18f;
nameplateSettings.DefaultColor = Color.white;
nameplateSettings.OutlineWidth = 0.15f;
nameplateSettings.OutlineColor = Color.black;
nameplateSettings.VerticalOffset = 2.5f;
nameplateSettings.CullDistance = 50f;
nameplateSettings.FadeStartDistance = 30f;
nameplateSettings.IconSize = new Vector2(20, 20);
AssetDatabase.CreateAsset(nameplateSettings, "Assets/Settings/Display/NameplateSettings.asset");

var damageSettings = ScriptableObject.CreateInstance<FloatTextSettings>();
damageSettings.Type = FloatTextType.Normal;
damageSettings.Color = new Color(1f, 0.27f, 0.27f);
damageSettings.FontSize = 36f;
damageSettings.Duration = 0.8f;
damageSettings.MoveUpDistance = 50f;
damageSettings.FadeStartRatio = 0.5f;
damageSettings.StartScale = 1f;
AssetDatabase.CreateAsset(damageSettings, "Assets/Settings/Display/FloatText_Damage.asset");

var critSettings = ScriptableObject.CreateInstance<FloatTextSettings>();
critSettings.Type = FloatTextType.Crit;
critSettings.Color = new Color(1f, 0.53f, 0f);
critSettings.FontSize = 42f;
critSettings.Duration = 1.2f;
critSettings.MoveUpDistance = 70f;
critSettings.FadeStartRatio = 0.6f;
critSettings.StartScale = 0.6f;
AssetDatabase.CreateAsset(critSettings, "Assets/Settings/Display/FloatText_CritDamage.asset");

var healSettings = ScriptableObject.CreateInstance<FloatTextSettings>();
healSettings.Type = FloatTextType.Heal;
healSettings.Color = new Color(0.27f, 1f, 0.27f);
healSettings.FontSize = 32f;
healSettings.Duration = 1f;
healSettings.MoveUpDistance = 40f;
healSettings.FadeStartRatio = 0.5f;
healSettings.StartScale = 1f;
AssetDatabase.CreateAsset(healSettings, "Assets/Settings/Display/FloatText_Heal.asset");

var dodgeSettings = ScriptableObject.CreateInstance<FloatTextSettings>();
dodgeSettings.Type = FloatTextType.Dodge;
dodgeSettings.Color = Color.white;
dodgeSettings.FontSize = 28f;
dodgeSettings.Duration = 0.6f;
dodgeSettings.MoveUpDistance = 40f;
dodgeSettings.FadeStartRatio = 0.5f;
dodgeSettings.StartScale = 1f;
AssetDatabase.CreateAsset(dodgeSettings, "Assets/Settings/Display/FloatText_Dodge.asset");

var blockSettings = ScriptableObject.CreateInstance<FloatTextSettings>();
blockSettings.Type = FloatTextType.Block;
blockSettings.Color = new Color(1f, 0.84f, 0f);
blockSettings.FontSize = 28f;
blockSettings.Duration = 0.6f;
blockSettings.MoveUpDistance = 40f;
blockSettings.FadeStartRatio = 0.5f;
blockSettings.StartScale = 1f;
AssetDatabase.CreateAsset(blockSettings, "Assets/Settings/Display/FloatText_Block.asset");

var dotSettings = ScriptableObject.CreateInstance<FloatTextSettings>();
dotSettings.Type = FloatTextType.DOT;
dotSettings.Color = new Color(0.8f, 0.8f, 0.8f);
dotSettings.FontSize = 22f;
dotSettings.Duration = 0.5f;
dotSettings.MoveUpDistance = 20f;
dotSettings.FadeStartRatio = 0.5f;
dotSettings.StartScale = 1f;
AssetDatabase.CreateAsset(dotSettings, "Assets/Settings/Display/FloatText_DOT.asset");

var skillSettings = ScriptableObject.CreateInstance<FloatTextSettings>();
skillSettings.Type = FloatTextType.SkillName;
skillSettings.Color = new Color(1f, 0.84f, 0f);
skillSettings.FontSize = 28f;
skillSettings.Duration = 1.5f;
skillSettings.MoveUpDistance = 20f;
skillSettings.FadeStartRatio = 0.6f;
skillSettings.StartScale = 0.5f;
AssetDatabase.CreateAsset(skillSettings, "Assets/Settings/Display/FloatText_SkillName.asset");

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
```

- [ ] **Step 3: Verify assets exist**

Use `assets-find` with filter `t:NameplateSettings` and `t:FloatTextSettings`. Expected: 1 NameplateSettings, 7 FloatTextSettings found.

- [ ] **Step 4: Commit**

```bash
git add Assets/Settings/Display/
git commit -m "feat: create display settings ScriptableObject assets"
```

---

### Task 13: Wire up EntityDisplayManager in scene

**Files:**
- Modify: Scene GameObject holding EntityDisplayManager component

The new EntityDisplayManager has 4 `[SerializeField]` fields that must be assigned.

- [ ] **Step 1: Find the EntityDisplayManager GameObject**

Use `gameobject-find` with name `EntityDisplayManager` in the active scene.

- [ ] **Step 2: Assign settings references via component modify**

For each serialized field on the EntityDisplayManager component, assign the corresponding asset:

| Field | Asset Path |
|-------|-----------|
| `_nameplateSettings` | `Assets/Settings/Display/NameplateSettings.asset` |
| `_damageSettings` | `Assets/Settings/Display/FloatText_Damage.asset` |
| `_critDamageSettings` | `Assets/Settings/Display/FloatText_CritDamage.asset` |
| `_skillNameSettings` | `Assets/Settings/Display/FloatText_SkillName.asset` |

Use `gameobject-component-modify` with `pathPatches` to set each field, or use `script-execute` to assign them via code:
```csharp
var mgr = Object.FindObjectOfType<EntityDisplayManager>();
var serializedObject = new SerializedObject(mgr);
serializedObject.FindProperty("_nameplateSettings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<NameplateSettings>("Assets/Settings/Display/NameplateSettings.asset");
serializedObject.FindProperty("_damageSettings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<FloatTextSettings>("Assets/Settings/Display/FloatText_Damage.asset");
serializedObject.FindProperty("_critDamageSettings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<FloatTextSettings>("Assets/Settings/Display/FloatText_CritDamage.asset");
serializedObject.FindProperty("_skillNameSettings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<FloatTextSettings>("Assets/Settings/Display/FloatText_SkillName.asset");
serializedObject.ApplyModifiedProperties();
```

- [ ] **Step 3: Enter Play Mode and verify**

Run `editor-application-set-state` to enter play mode. Check `console-get-logs` for errors. Expected: no NullReferenceException on `_nameplateSettings`, nameplates appear when monsters are present, damage events produce float text.

- [ ] **Step 4: Commit scene changes**

```bash
git add Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "fix: wire EntityDisplayManager settings references in scene"
```

---

### Task 14: Final verification and cleanup

- [ ] **Step 1: Run full compilation check**

Run `assets-refresh`. Check `console-get-logs` filter Error. Expected: zero errors.

- [ ] **Step 2: Verify no stale references**

Run grep for `FloatTextPresets` and `ColorPalette` in `Assets/Scripts/`:
```bash
grep -r "FloatTextPresets\|ColorPalette" Assets/Scripts/
```
Expected: no results (except possibly in comments).

- [ ] **Step 3: Run EditMode tests if available**

Run `tests-run` with testMode EditMode. Expected: any existing tests still pass (the display system has no unit tests currently).
