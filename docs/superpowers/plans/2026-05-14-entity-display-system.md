# Entity Display System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace NameplateManager + FloatingTextPool + NpcMirror with a unified EntityDisplayManager on a single ScreenSpaceOverlay Canvas.

**Architecture:** EntityDisplayManager (MonoBehaviour Singleton) manages two TMP pools on one ScreenSpaceOverlay Canvas — NameplatePool for persistent nameplates (register→unregister lifecycle) and FloatTextPool for temporary floating text (spawn→animate→auto-return). Distance culling, alpha fade, multi-hit merge, and screen shake all live in the manager.

**Tech Stack:** Unity 2022.3, TextMeshPro, DOTween (DOPunchScale, DOPunchPosition, DOFade, DOAnchorPos), existing EventBus from Sys3C.Core

**Note:** No Unity Test Framework infrastructure exists in this project. Verification is via Unity compilation (script-execute or assets-refresh) and play-mode observation.

---

## File Structure

| Action | File | Purpose |
|--------|------|---------|
| Create | `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs` | Main manager: singleton, canvas, pools, event handling, LateUpdate |
| Create | `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateConfig.cs` | Nameplate config struct |
| Create | `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextConfig.cs` | Float text type enum + preset configs |
| Create | `Assets/Scripts/Hotfix/GameSystems/Nameplate/ColorPalette.cs` | Static entity/profession color definitions |
| Modify | `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs` | Add EntityId to MonsterTakeDamageEvent |
| Modify | `Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs` | Add WasCritical property |
| Modify | `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs` | Register nameplate in Init(), emit isCrit in TakeDamage |
| Delete | `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateManager.cs` | Replaced |
| Delete | `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateTag.cs` | Replaced |
| Delete | `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatingTextPool.cs` | Replaced |
| Delete | `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatingTextConfig.cs` | Replaced |
| Delete | `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateEventBridge.cs` | Replaced |
| Delete | `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirrorManager.cs` | Dead code |
| Delete | `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirrorComponent.cs` | Dead code |
| Delete | `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcAnimationController.cs` | Dead code |
| Delete | `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMessages.cs` | Dead code |
| Delete | `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirror.asmdef` | Dead code |

---

### Task 1: Delete NpcMirror dead code

**Files:**
- Delete: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirrorManager.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirrorComponent.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcAnimationController.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMessages.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMessages.cs.meta`
- Delete: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirror.asmdef`
- Delete: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirror.asmdef.meta`

- [ ] **Step 1: Delete NpcMirror files via MCP**

Use `assets-delete` to delete the five files. Use `assets-find` with filter `NpcMirror t:Script` and `NpcMirror t:AssemblyDefinitionAsset` to locate them, then delete each.

- [ ] **Step 2: Refresh and verify compilation**

Run `assets-refresh`. Check console for compilation errors — there should be none since no other code references NpcMirror.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/NpcMirror/
git commit -m "chore: delete dead NpcMirror client code (never wired to network layer)"
```

---

### Task 2: Create NameplateConfig.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateConfig.cs`

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public struct NameplateConfig
    {
        public string DisplayName;
        public Color NameColor;
        public Sprite ClassIcon;
        public float VerticalOffset;
        public float CullDistance;

        public NameplateConfig(string displayName, Color? color = null)
        {
            DisplayName = displayName;
            NameColor = color ?? Color.white;
            ClassIcon = null;
            VerticalOffset = 2.5f;
            CullDistance = 0f; // 0 = use global default
        }
    }
}
```

- [ ] **Step 2: Refresh assets and verify**

Run `assets-refresh`. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateConfig.cs*
git commit -m "feat: add NameplateConfig struct"
```

---

### Task 3: Create FloatTextConfig.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextConfig.cs`

- [ ] **Step 1: Write the file**

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
        public FloatTextType Type;
        public Color Color = Color.white;
        public float FontSize = 36f;
        public float Duration = 1f;
        public float MoveUpDistance = 50f;
        public bool ShowName;
        public string TextOverride;
    }

    public static class FloatTextPresets
    {
        public static FloatTextConfig Damage => new()
        {
            Type = FloatTextType.Normal,
            Color = new Color(1f, 0.27f, 0.27f),
            FontSize = 36f,
            Duration = 0.8f,
            MoveUpDistance = 50f
        };

        public static FloatTextConfig CritDamage => new()
        {
            Type = FloatTextType.Crit,
            Color = new Color(1f, 0.53f, 0f),
            FontSize = 42f,
            Duration = 1.2f,
            MoveUpDistance = 70f
        };

        public static FloatTextConfig Heal => new()
        {
            Type = FloatTextType.Heal,
            Color = new Color(0.27f, 1f, 0.27f),
            FontSize = 32f,
            Duration = 1f,
            MoveUpDistance = 40f
        };

        public static FloatTextConfig Dodge => new()
        {
            Type = FloatTextType.Dodge,
            Color = Color.white,
            FontSize = 28f,
            Duration = 0.6f,
            MoveUpDistance = 40f,
            TextOverride = "闪避"
        };

        public static FloatTextConfig Block => new()
        {
            Type = FloatTextType.Block,
            Color = new Color(1f, 0.84f, 0f),
            FontSize = 28f,
            Duration = 0.6f,
            MoveUpDistance = 40f,
            TextOverride = "格挡"
        };

        public static FloatTextConfig DOT => new()
        {
            Type = FloatTextType.DOT,
            Color = new Color(0.8f, 0.8f, 0.8f),
            FontSize = 22f,
            Duration = 0.5f,
            MoveUpDistance = 20f
        };

        public static FloatTextConfig SkillName(string name) => new()
        {
            Type = FloatTextType.SkillName,
            Color = new Color(1f, 0.84f, 0f),
            FontSize = 28f,
            Duration = 1.5f,
            MoveUpDistance = 20f,
            ShowName = true,
            TextOverride = name
        };
    }
}
```

- [ ] **Step 2: Refresh assets and verify**

Run `assets-refresh`. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextConfig.cs*
git commit -m "feat: add FloatTextType enum and FloatTextPresets"
```

---

### Task 4: Create ColorPalette.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/ColorPalette.cs`

- [ ] **Step 1: Write the file**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public static class ColorPalette
    {
        // Entity nameplate colors
        public static readonly Color Npc = Color.white;
        public static readonly Color Monster = new Color(1f, 0.5f, 0.3f);
        public static readonly Color Player = new Color(0.3f, 1f, 0.3f);

        // Profession colors
        public static readonly Color Warrior = new Color(0.85f, 0.33f, 0.28f);
        public static readonly Color Mage = new Color(0.28f, 0.55f, 1f);
        public static readonly Color Priest = Color.white;
        public static readonly Color Rogue = new Color(1f, 0.9f, 0.3f);

        public static Color ForProfession(int professionId)
        {
            return professionId switch
            {
                1 => Warrior,
                2 => Mage,
                3 => Priest,
                4 => Rogue,
                _ => Npc
            };
        }
    }
}
```

- [ ] **Step 2: Refresh assets and verify**

Run `assets-refresh`. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/ColorPalette.cs*
git commit -m "feat: add ColorPalette for entity/profession colors"
```

---

### Task 5: Add EntityId to MonsterTakeDamageEvent

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs`

- [ ] **Step 1: Add EntityId field to MonsterTakeDamageEvent**

Read the file, then replace the MonsterTakeDamageEvent struct:

```csharp
/// <summary>
/// 怪物受伤事件（给浮字系统使用）
/// </summary>
public struct MonsterTakeDamageEvent : IEvent
{
    public int EntityId;
    public Vector3 HitPosition;
    public int Damage;
    public bool IsCritical;

    public MonsterTakeDamageEvent(int entityId, Vector3 hitPos, int damage, bool isCritical = false)
    {
        EntityId = entityId;
        HitPosition = hitPos;
        Damage = damage;
        IsCritical = isCritical;
    }
}
```

- [ ] **Step 2: Refresh assets and verify**

Run `assets-refresh`. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs
git commit -m "feat: add EntityId to MonsterTakeDamageEvent for float text merge"
```

---

### Task 6: Create EntityDisplayManager.cs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs`

This is the core file (~280 lines). It goes in one task because it's a single responsibility.

- [ ] **Step 1: Write the complete file**

```csharp
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Events;

namespace Hotfix.GameSystems.Nameplate
{
    public class EntityDisplayManager : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _fontAsset;
        [SerializeField] private Material _fontMaterial;
        [SerializeField] private float _cullDistance = 50f;
        [SerializeField] private float _fadeStartDistance = 30f;

        // Canvas
        private Canvas _canvas;
        private Camera _camera;

        // Nameplate pools
        private readonly Stack<GameObject> _nameplateFree = new();
        private readonly Dictionary<int, DisplayEntry> _entries = new();

        // Float text pools
        private readonly Stack<TextMeshProUGUI> _floatTextFree = new();
        private readonly HashSet<TextMeshProUGUI> _floatTextActive = new();

        // Merge tracking: key = (entityId, type)
        private readonly Dictionary<long, MergeEntry> _mergeTracker = new();

        // Screen shake cooldown
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

        // ===== Unity Lifecycle =====

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

                // World → screen position
                var worldPos = entry.Owner.position + Vector3.up * entry.Config.VerticalOffset;
                var screenPos = _camera.WorldToScreenPoint(worldPos);
                if (screenPos.z > 0)
                    entry.Root.transform.position = screenPos;

                // Distance alpha fade
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

            // Clean expired merge entries
            var expiredKeys = new List<long>();
            foreach (var kv in _mergeTracker)
                if (Time.time - kv.Value.LastHitTime > MergeWindow)
                    expiredKeys.Add(kv.Key);
            foreach (var k in expiredKeys)
                _mergeTracker.Remove(k);
        }

        private void OnDestroy()
        {
            // Clean up all active float texts
            foreach (var tmp in _floatTextActive)
                if (tmp != null) Destroy(tmp.gameObject);
            _floatTextActive.Clear();

            foreach (var tmp in _floatTextFree)
                if (tmp != null) Destroy(tmp.gameObject);
            _floatTextFree.Clear();

            // Clean up nameplates
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

            // Check merge window
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

            // New float text
            var tmp = SpawnFloatText(worldPos, config, value);

            // Track for merge (only for damage types that accumulate)
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

            // Screen shake for crit
            if (config.Type == FloatTextType.Crit && _camera != null
                && Time.time - _lastShakeTime > ShakeCooldown)
            {
                _lastShakeTime = Time.time;
                _camera.transform.DOPunchPosition(new Vector3(2f, 1f, 0f), 0.15f, 5, 0.5f);
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
                return PopAndActivate(_nameplateFree);

            return CreateNameplateTemplate();
        }

        private static GameObject PopAndActivate(Stack<GameObject> pool)
        {
            var go = pool.Pop();
            go.SetActive(true);
            return go;
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

            root.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            root.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Icon
            var iconGo = new GameObject("ClassIcon");
            iconGo.transform.SetParent(root.transform, false);
            var icon = iconGo.AddComponent<Image>();
            icon.rectTransform.sizeDelta = new Vector2(20, 20);
            icon.enabled = false;

            // Name text
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

            // Text content
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

            // Position to screen
            if (_camera != null)
            {
                var screenPos = _camera.WorldToScreenPoint(worldPos);
                tmp.rectTransform.position = screenPos;
            }

            // Animation
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

                default: // Normal, Heal, DOT
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
```

- [ ] **Step 2: Refresh assets and verify**

Run `assets-refresh`. Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs*
git commit -m "feat: add EntityDisplayManager (unified nameplate + float text system)"
```

---

### Task 7: Wire up MonsterEntity

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

- [ ] **Step 1: Add EntityDisplayManager registration in Init()**

At the end of `MonsterEntity.Init()`, add:

```csharp
// Register nameplate with EntityDisplayManager
var displayMgr = EntityDisplayManager.Instance;
if (displayMgr != null && !string.IsNullOrEmpty(_config.DisplayName))
{
    var nameplateCfg = new NameplateConfig(_config.DisplayName, ColorPalette.Monster);
    displayMgr.Register(GetInstanceID(), transform, nameplateCfg);
}
```

- [ ] **Step 2: Add unregistration in OnDestroy()**

In `OnDestroy()`, add before `PhysicsRegistry.Instance.Unregister(this)`:

```csharp
// Unregister nameplate
EntityDisplayManager.Instance?.Unregister(GetInstanceID());
```

- [ ] **Step 3: Pass IsCritical to MonsterTakeDamageEvent**

In `IDamageable.TakeDamage()`, replace the `EventBus.Emit(new MonsterTakeDamageEvent(...))` call. The current code is:

```csharp
EventBus.Emit(new MonsterTakeDamageEvent(
    transform.position + Vector3.up * 2f,
    Mathf.CeilToInt(data.BaseDamage)
));
```

Replace with:

```csharp
EventBus.Emit(new MonsterTakeDamageEvent(
    GetInstanceID(),
    transform.position + Vector3.up * 2f,
    Mathf.CeilToInt(data.BaseDamage),
    data.CriticalRateBonus > 0 // approximate: DamageBlock has crit rate but doesn't expose the roll result
));
```

- [ ] **Step 3.5: Expose WasCritical in DamageBlock**

In `Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs`, add a public property:

```csharp
// After the existing properties (after line 68):
public bool WasCritical { get; private set; }
```

In the `CalculateFinalDamage` method, inside the crit block (around line 83-87), set it when crit triggers. Replace:

```csharp
if (UnityEngine.Random.value < critChance)
{
    damage *= (1f + 1.5f + _criticalDamageBonus);
}
```

With:

```csharp
if (UnityEngine.Random.value < critChance)
{
    WasCritical = true;
    damage *= (1f + 1.5f + _criticalDamageBonus);
}
else
{
    WasCritical = false;
}
```

Then in MonsterEntity.TakeDamage(), await `_stats.TakeDamage(data)` returns, pass `data.WasCritical`:

```csharp
EventBus.Emit(new MonsterTakeDamageEvent(
    GetInstanceID(),
    transform.position + Vector3.up * 2f,
    Mathf.CeilToInt(data.BaseDamage),
    data.WasCritical
));
```

- [ ] **Step 4: Refresh assets and verify**

Run `assets-refresh`. Expected: no compilation errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs
git commit -m "feat: connect MonsterEntity to EntityDisplayManager for nameplates"
```

---

### Task 8: Delete old Nameplate files

**Files:**
- Delete: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateManager.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateTag.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatingTextPool.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatingTextConfig.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateEventBridge.cs`
- Plus their `.meta` files

- [ ] **Step 1: Delete old files via MCP**

Use `assets-delete` to delete each of the five files (plus .meta files). First find them with `assets-find`.

- [ ] **Step 2: Refresh and verify compilation**

Run `assets-refresh`. Expected: no compilation errors — the only references to these classes were in their own files and in MonsterEntity (already updated).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/
git commit -m "refactor: remove old NameplateManager, FloatingTextPool, NameplateEventBridge"
```

---

### Task 9: Verify in Unity scene

- [ ] **Step 1: Check that EntityDisplayManager GameObject exists in the scene**

The manager is a MonoBehaviour singleton with `DontDestroyOnLoad`. It must be placed on a GameObject in the initial scene, or auto-created. Since the old `NameplateManager` and `FloatingTextPool` were also MonoBehaviour singletons and presumably exist in-scene, check the current scene setup:

Run `gameobject-find` with name `NameplateManager` and `FloatingTextPool` to see how they were set up. The new `EntityDisplayManager` component can replace them on a single GameObject.

- [ ] **Step 2: Enter Play Mode and verify**

Use `editor-application-set-state` to enter play mode. Check `console-get-logs` for errors. Then exit play mode.

Expected: No exceptions. Nameplates appear above monsters (if monsters are present in scene). Damage events trigger floating text.

- [ ] **Step 3: Commit any scene changes**

```bash
git add Assets/Scenes/
git commit -m "fix: replace old nameplate managers with EntityDisplayManager in scene"
```
