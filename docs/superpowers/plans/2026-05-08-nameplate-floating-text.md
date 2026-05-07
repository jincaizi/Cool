# Nameplate & Floating Text System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build TMP 3D nameplates, floating damage/skill text pool, and target panel — performance-first world-space UI.

**Architecture:** NameplateManager manages TMP 3D nameplates via Dictionary<Transform, TMP>. FloatingTextPool manages a growable pool of Screen Space TMP objects with DOTween animations. EventBus hooks drive damage/skill text spawning.

**Tech Stack:** Unity 2022.3 LTS, TMP 3.0.6, DOTween, UniTask, UGUI

**Prerequisites:** TMP already installed (3.0.6). MonsterConfig.DisplayName exists.

---

### Task 1: Create Nameplate Assembly and FloatingTextConfig

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/Nameplate.asmdef`
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatingTextConfig.cs`

Foundation: assembly definition and config data types.

- [ ] **Step 1: Create Nameplate.asmdef**

```json
{
    "name": "Hotfix.GameSystems.Nameplate",
    "rootNamespace": "Hotfix.GameSystems.Nameplate",
    "references": [
        "Core",
        "UniTask",
        "DOTween.Modules",
        "Hotfix.GameSystems.UI",
        "Hotfix.GameSystems.Skills"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Create FloatingTextConfig.cs**

```csharp
using System;
using DG.Tweening;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    [Serializable]
    public class FloatingTextConfig
    {
        public Color Color = Color.white;
        public float FontSize = 36f;
        public float Duration = 1f;
        public float MoveUpDistance = 50f;
        public float StartScale = 1f;
        public bool PunchScale;
        public Ease Ease = Ease.OutCubic;
    }

    public static class FloatingTextPresets
    {
        public static readonly FloatingTextConfig Damage = new()
        {
            Color = new Color(1f, 0.27f, 0.27f),
            FontSize = 36f,
            Duration = 1f,
            MoveUpDistance = 50f,
            Ease = Ease.OutCubic
        };

        public static readonly FloatingTextConfig CritDamage = new()
        {
            Color = new Color(1f, 0.53f, 0f),
            FontSize = 42f,
            Duration = 1.2f,
            MoveUpDistance = 70f,
            PunchScale = true,
            Ease = Ease.OutBack
        };

        public static readonly FloatingTextConfig Heal = new()
        {
            Color = new Color(0.27f, 1f, 0.27f),
            FontSize = 32f,
            Duration = 1f,
            MoveUpDistance = 40f,
            Ease = Ease.OutCubic
        };

        public static readonly FloatingTextConfig SkillName = new()
        {
            Color = new Color(1f, 0.84f, 0f),
            FontSize = 28f,
            Duration = 1.5f,
            MoveUpDistance = 20f,
            StartScale = 0.8f,
            Ease = Ease.OutCubic
        };
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/Nameplate.asmdef \
        Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatingTextConfig.cs
git commit -m "$(cat <<'EOF'
feat: add Nameplate asmdef and FloatingTextConfig with presets
EOF
)"
```

---

### Task 2: Create FloatingTextPool

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatingTextPool.cs`

Growable object pool for floating text with DOTween animations.

- [ ] **Step 1: Create FloatingTextPool.cs**

```csharp
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class FloatingTextPool : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _fontAsset;
        [SerializeField] private Material _fontMaterial;
        [SerializeField] private int _preWarmCount = 10;

        private readonly Stack<TextMeshProUGUI> _free = new Stack<TextMeshProUGUI>();
        private readonly HashSet<TextMeshProUGUI> _active = new HashSet<TextMeshProUGUI>();
        private Canvas _canvas;
        private Camera _camera;
        private const int GrowSize = 10;

        public static FloatingTextPool Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _camera = Camera.main;
            CreateCanvas();
            PreWarm(_preWarmCount);
        }

        private void CreateCanvas()
        {
            var go = new GameObject("FloatingTextCanvas");
            go.transform.SetParent(transform);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 4500;
            go.AddComponent<UnityEngine.UI.CanvasScaler>();
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        private void PreWarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var tmp = CreateTMP();
                tmp.gameObject.SetActive(false);
                _free.Push(tmp);
            }
        }

        private TextMeshProUGUI CreateTMP()
        {
            var go = new GameObject("FloatingText");
            go.transform.SetParent(_canvas.transform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (_fontAsset != null) tmp.font = _fontAsset;
            if (_fontMaterial != null) tmp.fontMaterial = _fontMaterial;
            tmp.raycastTarget = false;
            tmp.alpha = 1f;
            return tmp;
        }

        private void Grow()
        {
            for (int i = 0; i < GrowSize; i++)
                _free.Push(CreateTMP());
        }

        public void Spawn(Vector3 worldPos, string text, FloatingTextConfig config)
        {
            if (_free.Count == 0) Grow();

            var tmp = _free.Pop();
            _active.Add(tmp);

            tmp.gameObject.SetActive(true);
            tmp.text = text;
            tmp.color = config.Color;
            tmp.fontSize = config.FontSize;
            tmp.alpha = 1f;

            // Position: WorldToScreenPoint
            if (_camera != null)
            {
                var screenPos = _camera.WorldToScreenPoint(worldPos);
                var rt = tmp.rectTransform;
                rt.position = screenPos;
                rt.localScale = Vector3.one * config.StartScale;
            }

            // DOTween animation
            var rt2 = tmp.rectTransform;
            var startY = rt2.anchoredPosition.y;
            var seq = DOTween.Sequence();

            if (config.PunchScale)
            {
                seq.Join(rt2.DOPunchScale(Vector3.one * 0.3f, config.Duration, 1, 0f));
            }
            if (config.StartScale != 1f)
            {
                rt2.localScale = Vector3.one * config.StartScale;
                seq.Join(rt2.DOScale(1f, config.Duration * 0.3f).SetEase(Ease.OutBack));
            }

            seq.Join(rt2.DOAnchorPosY(startY + config.MoveUpDistance, config.Duration)
                .SetEase(config.Ease));
            seq.Join(tmp.DOFade(0f, config.Duration * 0.7f).SetDelay(config.Duration * 0.3f));

            seq.OnKill(() =>
            {
                _active.Remove(tmp);
                tmp.alpha = 1f;
                tmp.text = "";
                rt2.localScale = Vector3.one;
                tmp.gameObject.SetActive(false);
                _free.Push(tmp);
            });

            seq.SetTarget(tmp.transform);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatingTextPool.cs
git commit -m "$(cat <<'EOF'
feat: add FloatingTextPool with growable pool and DOTween animations
EOF
)"
```

---

### Task 3: Create NameplateTag and NameplateManager

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateTag.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateManager.cs`

NameplateTag marks the mount point. NameplateManager manages all TMP 3D nameplates.

- [ ] **Step 1: Create NameplateTag.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class NameplateTag : MonoBehaviour
    {
        public Vector3 Offset = new Vector3(0, 2.5f, 0);
        public string DisplayName = "";
        public Color NameColor = Color.white;
    }
}
```

- [ ] **Step 2: Create NameplateManager.cs**

```csharp
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
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateTag.cs \
        Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateManager.cs
git commit -m "$(cat <<'EOF'
feat: add NameplateTag and NameplateManager for TMP 3D nameplates
EOF
)"
```

---

### Task 4: Add ITargetable, MonsterTakeDamageEvent, SkillActivatedEvent

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/ITargetable.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs`

Targetable interface and new EventBus event types.

- [ ] **Step 1: Create ITargetable.cs**

```csharp
using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface ITargetable
    {
        string DisplayName { get; }
        int Level { get; }
        Sprite Portrait { get; }
        float HPPercent { get; }
        int CurrentHP { get; }
        int MaxHP { get; }
        Vector3 WorldPosition { get; }
        event Action<float, int, int> OnHPChanged;
        event Action OnDeath;
    }
}
```

- [ ] **Step 2: Add new event structs to DamageEvents.cs**

Append after the existing code in DamageEvents.cs:

```csharp
    /// <summary>
    /// 怪物受伤事件（给浮字系统使用）
    /// </summary>
    public struct MonsterTakeDamageEvent : IEvent
    {
        public Vector3 HitPosition;
        public int Damage;
        public bool IsCritical;

        public MonsterTakeDamageEvent(Vector3 hitPos, int damage, bool isCritical = false)
        {
            HitPosition = hitPos;
            Damage = damage;
            IsCritical = isCritical;
        }
    }

    /// <summary>
    /// 技能激活事件（给浮字系统显示技能名）
    /// </summary>
    public struct SkillActivatedEvent : IEvent
    {
        public Vector3 CasterPosition;
        public string SkillName;

        public SkillActivatedEvent(Vector3 casterPos, string skillName)
        {
            CasterPosition = casterPos;
            SkillName = skillName;
        }
    }
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/ITargetable.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs
git commit -m "$(cat <<'EOF'
feat: add ITargetable interface, MonsterTakeDamageEvent, SkillActivatedEvent
EOF
)"
```

---

### Task 5: Modify MonsterEntity (ITargetable + Events)

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterConfig.cs`

Add ITargetable implementation and emit MonsterTakeDamageEvent.

- [ ] **Step 1: Add ITargetable to MonsterEntity**

Read current MonsterEntity.cs, then:

1. Add `using Hotfix.GameSystems.Sys3C.Core.Events;` to imports
2. Change class declaration to: `public class MonsterEntity : MonoBehaviour, IDamageable, ITargetable`
3. Add ITargetable members at the end of the class (before the closing brace):
```csharp
        // ===== ITargetable =====

        string ITargetable.DisplayName => _config != null ? _config.DisplayName : name;
        int ITargetable.Level => 1;
        Sprite ITargetable.Portrait => null;
        float ITargetable.HPPercent => _stats != null ? _stats.HP / _stats.MaxHP : 0f;
        int ITargetable.CurrentHP => _stats != null ? Mathf.CeilToInt(_stats.HP) : 0;
        int ITargetable.MaxHP => _stats != null ? Mathf.CeilToInt(_stats.MaxHP) : 0;
        Vector3 ITargetable.WorldPosition => transform.position;
        event Action<float, int, int> ITargetable.OnHPChanged;
        event Action ITargetable.OnDeath;

        void ITargetable.OnHPChangedHandler(float percent, int current, int max) { }
        void ITargetable.OnDeathHandler() { }
```

4. In Init(), after `_stats.OnHPChanged += (cur, max) => { };` (line 54), replace the lambda with:
```csharp
            _stats.OnHPChanged += (cur, max) =>
            {
                ((ITargetable)this).OnHPChanged?.Invoke(
                    max > 0 ? cur / max : 0f, Mathf.CeilToInt(cur), Mathf.CeilToInt(max));
            };
```

5. In Init(), add after the OnHPChanged line:
```csharp
            _stats.OnDeath += () => ((ITargetable)this).OnDeath?.Invoke();
```

Note: since _stats.OnDeath is already subscribed in Init() via `_stats.OnDeath += HandleDeath;`, the OnDeath callbacks will fire in order — first HandleDeath, then the ITargetable OnDeath event. Use the existing subscription pattern: add to _stats.OnDeath via +=.

6. In IDamageable.TakeDamage():
```csharp
        void IDamageable.TakeDamage(DamageData data, Vector3 hitDirection)
        {
            if (_stats.IsDead) return;
            _stats.TakeDamage(data);
            _ai.NotifyHit(data, hitDirection);

            // Emit monster damage event for floating text
            EventBus.Emit(new MonsterTakeDamageEvent(
                transform.position + Vector3.up * 2f,
                Mathf.CeilToInt(data.BaseDamage),
                data.IsCritical
            ));
        }
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "$(cat <<'EOF'
feat: add ITargetable to MonsterEntity, emit MonsterTakeDamageEvent
EOF
)"
```

---

### Task 6: Create TargetPanel

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/TargetPanel.cs`

UIPanel for target HP bar display.

- [ ] **Step 1: Create TargetPanel.cs**

```csharp
using Hotfix.GameSystems.Sys3C.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class TargetPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Base;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        public override string PanelId => "TargetPanel";

        [SerializeField] private Image _portrait;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _levelText;
        [SerializeField] private Slider _hpSlider;
        [SerializeField] private TMP_Text _hpText;
        [SerializeField] private GameObject _contentRoot;

        private ITargetable _currentTarget;

        public void Bind(ITargetable target)
        {
            Clear();
            _currentTarget = target;

            if (_nameText != null) _nameText.text = target.DisplayName;
            if (_levelText != null) _levelText.text = $"Lv.{target.Level}";
            if (_portrait != null && target.Portrait != null) _portrait.sprite = target.Portrait;

            UpdateHP(target.HPPercent, target.CurrentHP, target.MaxHP);
            target.OnHPChanged += UpdateHP;
            target.OnDeath += OnTargetDeath;

            if (_contentRoot != null) _contentRoot.SetActive(true);
        }

        public void Clear()
        {
            if (_currentTarget != null)
            {
                _currentTarget.OnHPChanged -= UpdateHP;
                _currentTarget.OnDeath -= OnTargetDeath;
                _currentTarget = null;
            }

            if (_contentRoot != null) _contentRoot.SetActive(false);
        }

        private void UpdateHP(float percent, int current, int max)
        {
            if (_hpSlider != null) _hpSlider.value = percent;
            if (_hpText != null) _hpText.text = $"{current}/{max}";
        }

        private void OnTargetDeath()
        {
            Clear();
            UIManager.Instance?.HideAlwaysAsync(PanelId);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Clear();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/TargetPanel.cs
git commit -m "$(cat <<'EOF'
feat: add TargetPanel UIPanel with ITargetable binding
EOF
)"
```

---

### Task 7: Create NameplateEventBridge

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateEventBridge.cs`

Subscribes to EventBus and routes damage/skill events to FloatingTextPool.

- [ ] **Step 1: Create NameplateEventBridge.cs**

```csharp
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class NameplateEventBridge : MonoBehaviour
    {
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

        private void OnPlayerDamaged(DamageEvent e)
        {
            var cfg = e.IsCritical ? FloatingTextPresets.CritDamage : FloatingTextPresets.Damage;
            // Player position approximation: use hit direction to offset above player
            var posEstimate = Vector3.up * 2f;
            FloatingTextPool.Instance?.Spawn(posEstimate, $"-{Mathf.CeilToInt(e.Damage)}", cfg);
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var cfg = e.IsCritical ? FloatingTextPresets.CritDamage : FloatingTextPresets.Damage;
            FloatingTextPool.Instance?.Spawn(e.HitPosition, $"-{e.Damage}", cfg);
        }

        private void OnSkillActivated(SkillActivatedEvent e)
        {
            FloatingTextPool.Instance?.Spawn(
                e.CasterPosition + Vector3.up * 2.5f,
                e.SkillName,
                FloatingTextPresets.SkillName
            );
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateEventBridge.cs
git commit -m "$(cat <<'EOF'
feat: add NameplateEventBridge to route damage/skill events to FloatingTextPool
EOF
)"
```

---

### Task 8: Emit SkillActivatedEvent from SkillExecutor

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs`

Emit skill activation event when skill starts.

- [ ] **Step 1: Modify SkillExecutor.cs**

Read SkillExecutor.cs. In `OnHitboxTriggered` (approximately line 165), add after the hitbox frame event:

```csharp
        private void OnHitboxTriggered(int frameIndex)
        {
            OnHitboxFrame?.Invoke(frameIndex);
            // Emit skill name for floating text on first hitbox frame
            if (frameIndex == 0)
            {
                var pos = _owner?.Transform != null ? _owner.Transform.position : Vector3.zero;
                EventBus.Emit(new SkillActivatedEvent(pos, _skillData.SkillName ?? _skillData.name));
            }
        }
```

Note: Read the actual `OnHitboxTriggered` method first to verify the structure, then add the EventBus emit call. Also add `using Hotfix.GameSystems.Sys3C.Core.Events;` and `using Hotfix.GameSystems.Sys3C.Core;` imports.

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs
git commit -m "$(cat <<'EOF'
feat: emit SkillActivatedEvent from SkillExecutor on first hitbox frame
EOF
)"
```

---

### Task 9: Verify Compilation

**Files:** None (verification only)

- [ ] **Step 1: Check all files exist**

```bash
find Assets/Scripts/Hotfix/GameSystems/Nameplate -type f | sort
find Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD -type f | sort
```

- [ ] **Step 2: Fix compilation errors and commit .meta files**

Check Unity Editor console for errors. Common issues:
- Missing assembly references in Nameplate.asmdef
- EventBus.Unsubscribe type mismatch (if EventBus doesn't have generic Unsubscribe, adjust)
- DOTween.Modules reference in Nameplate.asmdef (DOTween.Modules may need to be referenced differently)

Commit any fixes + .meta files:
```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/
git commit -m "$(cat <<'EOF'
fix: resolve compilation issues and add missing .meta files for nameplate system
EOF
)"
```

---

### Approval Gate

**What this system provides after Task 9:**
- TMP 3D nameplates above units (billboard, distance-culled, Dynamic Batching)
- Growable floating text pool (10 base, +10 per overflow, DOTween animations)
- 4 presets: Damage, CritDamage, Heal, SkillName
- TargetPanel UIPanel (ITargetable binding, HP updates, death cleanup)
- EventBus hooks for player damage, monster damage, and skill activation
- MonsterEntity implements ITargetable

**What this system does NOT cover:**
- TMP Font Asset creation (Unity Editor: Window > TextMeshPro > Font Asset Creator)
- Nameplate layer setup (Edit > Project Settings > Tags and Layers)
- TargetPanel Prefab creation (Unity Editor: create Prefab from TargetPanel code)
- Player ITargetable implementation (separate task for the player system)
- 3D health bars above monsters
