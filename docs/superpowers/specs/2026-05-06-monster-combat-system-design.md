# 打怪系统设计文档

**Date:** 2026-05-06
**Status:** Approved
**Version:** 1.0

---

## 一、设计决策总结

| 决策项 | 选择 | 理由 |
|--------|------|------|
| 怪物复杂度 | 标准 MMO 野怪（巡逻+追击+攻击+技能+受击+掉落） | 满足当前需求 |
| 命中检测 | 碰撞器检测（Trigger Collider） | 精确，符合动作游戏体验 |
| 网络模式 | 纯客户端本地，预留接口 | 服务端未就绪，架构预留后续接入 |
| 掉落系统 | 包含基础掉落 | 击杀→掉落是完整闭环 |
| 组件风格 | 独立轻量实现，共享数据结构 | 不用玩家3C组件，但复用 DamageData/AttributeType |
| AI实现 | 纯 C# FSM + 状态转换表 | 与项目 BaseFSM/AttackFSM/HitFSM 风格一致 |
| 网络同步策略 | 权限分离：IMonsterAuthority 接口 | 后续加 Remote 实现即可，AI不感知网络 |

---

## 二、架构总览

### 核心洞察

"打怪系统"本质上是两个已有系统的对接——玩家攻击产生伤害帧，怪物受击接收伤害：

```
玩家 AttackFSM（已有）                      怪物系统（新建）
  Attack1/Attack2/SkillQ/R       ←→      AI: Idle/Patrol/Chase/Attack/Hit/Death
  攻击动画 → 武器碰撞器激活      ←→      受击碰撞器 → TakeDamage()
  CharacterController.TakeDamage  ←→      怪物攻击碰撞器 → 对玩家造成伤害
```

缺失的环节：**攻击碰撞器 ↔ 受击碰撞器的物理检测**。

### 攻击-受击完整流程

```
玩家按攻击键
  → AttackFSM 进入 Attack1/2/SkillQ/R
  → 动画播放到伤害帧（Animation Event）
  → 武器上的 AttackHitbox 激活（短暂持续几帧）
  → Physics.Overlap 检测 MonsterHitZone
  → 命中 → 怪物 MonsterAI.TakeDamage(damageData)
  → MonsterStats 扣血 → 如果 HP<=0 → MonsterAI → Death → 掉落 → 延迟销毁

怪物追击玩家
  → MonsterAI Chase → 距离足够 → Attack
  → 怪物攻击动画 → 攻击帧激活 MonsterAttackHitbox
  → 检测到玩家
  → 调用 FSMManager.HandleDamage()（已有接口）
  → 玩家 HitFSM 处理受击 → CharacterController.TakeDamage（已有流程）
```

### 文件结构

```
Assets/Scripts/Hotfix/GameSystems/Monster/
├── MonsterEntity.cs           // 入口 MonoBehaviour，组装和驱动各子模块
├── MonsterStats.cs            // 属性管理（HP/Attack/Defense），复用 AttributeType
├── MonsterAI.cs               // AI状态机：Idle/Patrol/Chase/Attack/Hit/Death
├── MonsterMovement.cs         // NavMeshAgent 移动控制
├── MonsterHitZone.cs          // 受击碰撞器，引用 IDamageable
├── MonsterAttackHitbox.cs     // 攻击碰撞器，动画事件开关
├── MonsterLootTable.cs        // 掉落配置 ScriptableObject
├── MonsterConfig.cs           // 怪物模板 ScriptableObject
├── MonsterSpawner.cs          // 刷怪管理
└── MonsterEvents.cs           // MonsterDeathEvent, MonsterSpawnEvent 等

Assets/Scripts/Hotfix/GameSystems/Combat/  (跨系统的战斗基础设施)
├── AttackHitbox.cs             // 通用攻击碰撞器（玩家武器挂载）
├── PlayerHitZone.cs            // 玩家受击碰撞器
└── IDamageable.cs              // 伤害接口（玩家和怪物共用）
```

### 组件关系图

```
                          ┌──────────────────┐
                          │  MonsterConfig   │ (ScriptableObject)
                          │  HP/Attack/Speed │
                          │  PatrolRadius    │
                          │  ChaseRange      │
                          │  LootTable ref   │
                          └────────┬─────────┘
                                   │ 创建时读取
                                   ▼
┌──────────────────────────────────────────────────────────────┐
│                      MonsterEntity (MonoBehaviour)            │
│                                                              │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────┐ │
│  │  Stats   │  │    AI    │  │ Movement │  │   HitZone    │ │
│  │          │  │          │  │          │  │ (IDamageable)│ │
│  │ HP/ATK/  │  │ FSM 6状态│  │NavMesh   │  │              │ │
│  │ DEF/...  │  │ 状态转换 │  │Agent移动 │  │ OnTriggerStay│ │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └──────┬───────┘ │
│       │             │             │               │         │
│       │             │  驱动 ──────┘               │         │
│       │             │                             │         │
│  ┌────┴─────────────┴─────────────────────────────┴──────┐  │
│  │              Animator (1层简化)                        │  │
│  │  Idle / Walk / Attack / Hit / Death                   │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌──────────────────┐  ┌──────────────────┐                  │
│  │ AttackHitbox     │  │   LootTable      │                  │
│  │ (武器碰撞器)     │  │   (掉落配置)     │                  │
│  └──────────────────┘  └──────────────────┘                  │
└──────────────────────────────────────────────────────────────┘

与玩家系统的对接点：
  MonsterAttackHitbox ──检测──→ PlayerHitZone ──调用──→ FSMManager.HandleDamage()
  Player AttackHitbox  ──检测──→ MonsterHitZone ──调用──→ MonsterEntity.TakeDamage()
```

### 与现有系统的集成点

| 集成点 | 现有系统 | Monster系统对接方式 |
|--------|---------|-------------------|
| 共享接口 | — | 新建 `IDamageable`，怪物和玩家都实现 |
| 伤害数据结构 | `DamageData`, `AttributeType`, `DamageType` | 直接复用 |
| 怪物打玩家 | `FSMManager.HandleDamage()` | MonsterAttackHitbox 检测到玩家后调用 |
| 玩家打怪物 | `AttackHitbox`（新建，挂在玩家武器上） | 检测到 MonsterHitZone 后调用 `IDamageable.TakeDamage()` |
| 事件通知 | `EventBus` | Monster 发出 `MonsterDeathEvent`，供掉落/任务等系统订阅 |

---

## 三、MonsterAI 状态机

### 状态定义

```csharp
public enum MonsterAIState
{
    Idle,       // 待机，等待巡逻或发现玩家
    Patrol,     // 沿路径点移动
    Chase,      // 追击玩家
    Attack,     // 攻击玩家
    Hit,        // 受击（短暂中断，可打断除Death外的所有状态）
    Death       // 死亡
}
```

### 状态转换图

```
                    ┌─────────────────────────────────────────┐
                    │              MonsterAI                   │
                    │                                         │
    TakeDamage() ──→│           ┌──────────┐                  │
                    │           │ 任意状态  │──→ Hit ──→ 恢复  │
    Death ─────────→│           └──────────┘                  │
                    │                                         │
    Idle ──→ Patrol ──→ Idle                                  │
      │                  │                                    │
      └────→ Chase ←─────┘  (发现玩家)                        │
               │  │                                           │
               │  └────→ Attack (进入攻击范围)                 │
               │           │                                  │
               └───────────┘ (玩家逃出攻击范围但未脱战)        │
                                                                
    Hit ──→ Death (HP<=0)                                     │
    Death ──→ 掉落 → 延迟销毁                                  │
```

### 状态转换条件

| 当前状态 | 目标状态 | 条件 |
|---------|---------|------|
| Idle | Patrol | 巡逻路径非空 && 等待冷却完成 |
| Idle | Chase | 玩家进入检测范围 (distance < detectRange) |
| Patrol | Idle | 到达路径点 |
| Patrol | Chase | 玩家进入检测范围 |
| Chase | Idle | 玩家超出脱战范围 (distance > leaveRange)，回到出生点 |
| Chase | Attack | 玩家进入攻击范围 (distance < attackRange) |
| Attack | Chase | 玩家离开攻击范围但仍 < leaveRange |
| Attack | Idle | 玩家超出脱战范围 |
| Hit (任意非Death) | 恢复 | Hit动画结束 && HP > 0，回到被打断前的状态 |
| Hit | Death | HP <= 0 |
| Death | — | (终止) Death动画结束 → OnDeathComplete事件 |

### 各状态行为

| 状态 | 动画 | 移动 | 持续逻辑 |
|------|------|------|---------|
| Idle | Idle动画循环 | 原地不动 | 等待 idleDuration 冷却后尝试巡逻 |
| Patrol | Walk动画 | NavMeshAgent 移向路径点 | 到达路径点→下一个路径点，无更多→Idle |
| Chase | Run动画 | NavMeshAgent 追击目标 | 持续更新目标位置，面向目标 |
| Attack | Attack动画 | 停止移动，面向目标 | 攻击冷却计时，CD好了&&在范围内→再次Attack |
| Hit | Hit动画 | 停止移动 | 动画持续时间（由HitData决定） |
| Death | Death动画 | 停止移动 | 动画播放完成→事件通知 |

### 核心代码结构

```csharp
public class MonsterAI
{
    private MonsterAIState _state;
    private MonsterAIState _preHitState;  // 记住受击前状态，Hit结束后恢复

    private readonly MonsterMovement _movement;
    private readonly MonsterStats _stats;
    private readonly Animator _animator;
    private readonly Transform _self;
    private readonly MonsterConfig _config;

    private float _idleTimer;
    private float _attackCooldown;
    private int _patrolIndex;

    public Transform Target { get; set; }       // 由外部设置
    public bool IsHitThisFrame { get; set; }    // 由 MonsterHitZone 设置
    public HitData PendingHitData { get; set; }

    public event Action OnDeathComplete;
    public event Action OnAttackFrame;          // 攻击伤害帧，触发 AttackHitbox

    public void Update(float deltaTime)
    {
        // 1. 评估转换表
        // 2. switch 执行当前状态行为
    }
}
```

---

## 四、MonsterStats（属性管理）

```csharp
public class MonsterStats
{
    private readonly Dictionary<AttributeType, float> _attributes = new();

    public float HP => _attributes[AttributeType.Health];
    public float MaxHP { get; private set; }
    public float AttackPower => _attributes[AttributeType.AttackPower];
    public float Defense => _attributes[AttributeType.Defense];
    public bool IsDead => HP <= 0;

    public event Action OnDeath;
    public event Action<float, float> OnHPChanged;  // current, max

    public MonsterStats(MonsterConfig config)
    {
        _attributes[AttributeType.Health] = config.MaxHP;
        _attributes[AttributeType.AttackPower] = config.AttackPower;
        _attributes[AttributeType.Defense] = config.Defense;
        _attributes[AttributeType.Speed] = config.MoveSpeed;
        MaxHP = config.MaxHP;
    }

    public void TakeDamage(DamageData damageData)
    {
        if (IsDead) return;

        float def = _attributes[AttributeType.Defense];
        float rawDamage = damageData.BaseDamage;
        float finalDamage = Mathf.Max(1, rawDamage - def * 0.3f);

        _attributes[AttributeType.Health] -= finalDamage;
        OnHPChanged?.Invoke(HP, MaxHP);

        if (HP <= 0)
        {
            _attributes[AttributeType.Health] = 0;
            OnDeath?.Invoke();
        }
    }
}
```

**说明：**
- 直接复用已有 `AttributeType` / `DamageData`
- 伤害公式先做简易版（攻击-防御），后续可替换为 `DamageCalculator`
- OnDeath / OnHPChanged 由 MonsterEntity 订阅，驱动 AI 和 HUD 血条

---

## 五、MonsterMovement（移动控制）

```csharp
public class MonsterMovement
{
    private readonly NavMeshAgent _agent;
    private readonly Transform _self;
    private readonly MonsterConfig _config;

    public bool HasReachedDestination =>
        !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.1f;

    public void Stop()  => _agent.isStopped = true;
    public void Resume() => _agent.isStopped = false;

    public void Chase(Transform target)    => _agent.SetDestination(target.position);
    public void PatrolTo(Vector3 point)    => _agent.SetDestination(point);
    public void ReturnToSpawn(Vector3 sp)  => _agent.SetDestination(sp);

    public void LookAt(Vector3 target)
    {
        Vector3 dir = target - _self.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.01f)
            _self.rotation = Quaternion.Slerp(_self.rotation,
                Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }
}
```

---

## 六、IDamageable + 碰撞器

### IDamageable（共享接口）

```csharp
public interface IDamageable
{
    void TakeDamage(DamageData damageData, Vector3 hitDirection);
    bool IsAlive { get; }
    Transform Transform { get; }
}
```

### MonsterHitZone

挂载在怪物身上或子物体上，碰撞器设为 Trigger。检测到 AttackHitbox 后通知对方命中。

### AttackHitbox（通用，玩家和怪物共用）

```csharp
public class AttackHitbox : MonoBehaviour
{
    public bool IsActive { get; private set; }
    private DamageData _damageData;
    private HashSet<IDamageable> _hitTargets;  // 每次激活每个目标只命中一次

    public void Activate(DamageData data)
    {
        IsActive = true;
        _damageData = data;
        _hitTargets.Clear();
        gameObject.SetActive(true);
    }

    public void Deactivate()
    {
        IsActive = false;
        gameObject.SetActive(false);
    }

    public void NotifyHit(IDamageable target)
    {
        if (!IsActive || _hitTargets.Contains(target)) return;
        _hitTargets.Add(target);

        Vector3 dir = (target.Transform.position - transform.position).normalized;
        target.TakeDamage(_damageData, dir);
    }
}
```

激活时机：Animation Event 在伤害帧调用 Activate/Deactivate，或通过 StateMachineBehaviour。

---

## 七、配置资产

### MonsterConfig（怪物模板 ScriptableObject）

```csharp
[CreateAssetMenu(fileName = "MonsterConfig", menuName = "Game/Monster")]
public class MonsterConfig : ScriptableObject
{
    public string MonsterId;
    public string DisplayName;
    public GameObject Prefab;

    // Stats
    public float MaxHP = 100;
    public float AttackPower = 20;
    public float Defense = 10;
    public float MoveSpeed = 3.5f;

    // AI Ranges
    public float DetectRange = 10f;
    public float LeaveRange = 15f;
    public float AttackRange = 2f;
    public float AttackCooldown = 1.5f;

    // Patrol
    public float PatrolRadius = 5f;
    public float IdleDuration = 2f;

    // Combat
    public DamageData AttackDamage;    // 复用已有 DamageData
    public float KnockbackForce;

    // Loot & Death
    public MonsterLootTable LootTable;
    public float DeathDestroyDelay = 3f;
}
```

### MonsterLootTable（掉落配置 ScriptableObject）

```csharp
[CreateAssetMenu(fileName = "LootTable", menuName = "Game/Monster/LootTable")]
public class MonsterLootTable : ScriptableObject
{
    public LootEntry[] Entries;
    public int GoldMin = 5;
    public int GoldMax = 20;

    public List<LootResult> Roll() { /* 随机掉落计算 */ }
}
```

---

## 八、MonsterEntity（入口组装）

```csharp
public class MonsterEntity : MonoBehaviour, IDamageable
{
    public Animator Animator;
    public NavMeshAgent NavAgent;
    public MonsterHitZone HitZone;
    public MonsterAttackHitbox AttackHitbox;

    private MonsterConfig _config;
    private MonsterStats _stats;
    private MonsterAI _ai;
    private MonsterMovement _movement;
    private Vector3 _spawnPoint;

    public event Action OnDeathComplete;          // 通知 Spawner
    public event Action<LootResult[]> OnLootDrop; // 通知掉落展示

    bool IDamageable.IsAlive => !_stats.IsDead;
    Transform IDamageable.Transform => transform;

    public void Init(MonsterConfig config, Vector3 spawnPoint)
    {
        _config = config;
        _spawnPoint = spawnPoint;

        _stats = new MonsterStats(config);
        _movement = new MonsterMovement(NavAgent, transform, config);
        _ai = new MonsterAI(this, _movement, _stats, Animator, config);
        HitZone.Init(this, _ai);

        _stats.OnDeath += HandleDeath;
        _ai.OnDeathComplete += () => StartCoroutine(DeathSequence());
        _ai.OnAttackFrame += () => AttackHitbox.Activate(config.AttackDamage);
    }

    void Update()
    {
        if (_stats.IsDead) return;
        _ai.Update(Time.deltaTime);
    }

    void IDamageable.TakeDamage(DamageData data, Vector3 hitDirection)
    {
        _stats.TakeDamage(data);
        _ai.NotifyHit(data, hitDirection);
    }

    private IEnumerator DeathSequence()
    {
        var loot = _config.LootTable?.Roll();
        if (loot?.Count > 0) OnLootDrop?.Invoke(loot.ToArray());
        yield return new WaitForSeconds(_config.DeathDestroyDelay);
        OnDeathComplete?.Invoke();
        Destroy(gameObject);
    }
}
```

---

## 九、MonsterSpawner（刷怪管理）

```csharp
public class MonsterSpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnGroup
    {
        public MonsterConfig Config;
        public int Count;
        public float SpawnRadius;
    }

    public SpawnGroup[] Groups;
    public float RespawnDelay = 30f;

    public void Spawn(MonsterConfig config, Vector3 position) { /* 实例化+Init */ }
}
```

---

## 十、网络同步预留

### 权限分离架构

```
MonsterAI (纯逻辑，不感知网络)
    ├── 状态转换决策
    └── 输出：移动目标、动画参数、伤害请求

IMonsterAuthority (权限接口)
    ├── LocalMonsterAuthority      ← 当前阶段
    │     AI 本地运行，HP 本地计算
    └── RemoteMonsterAuthority     ← 后续接入服务端
          AI 不运行，接收服务端状态快照
          PositionInterpolator 插值移动
          HP/动画状态由服务端推送
```

后续接入服务端时，只需加一层 `RemoteMonsterAuthority`，复用 `NpcMirrorManager` 进行多客户端位置/动画同步。MonsterAI 本身不需要任何网络代码。

---

## 十一、数据流总结

```
┌────────────────────────────────────────────────────────────────┐
│                        打怪完整数据流                            │
│                                                                │
│  ① 玩家攻击                                                   │
│  玩家按键 → AttackFSM → 动画伤害帧 → AttackHitbox.Activate()    │
│    → 检测 MonsterHitZone → AttackHitbox.NotifyHit()            │
│    → target.TakeDamage(damageData, direction)                   │
│                                                                │
│  ② 怪物受击                                                   │
│  MonsterEntity.TakeDamage()                                    │
│    → MonsterStats.TakeDamage() → HP减少 → OnHPChanged          │
│    → MonsterAI.NotifyHit() → 转换到 Hit 状态                    │
│    → Hit动画播放 → 结束 → 恢复到被打断前状态 (或Death)          │
│                                                                │
│  ③ 怪物攻击玩家                                               │
│  MonsterAI.Attack → 动画伤害帧 → MonsterAttackHitbox.Activate() │
│    → 检测 PlayerHitZone                                        │
│    → player.FSMManager.HandleDamage() → 玩家已有受击流程        │
│                                                                │
│  ④ 怪物死亡                                                   │
│  HP<=0 → MonsterStats.OnDeath                                  │
│    → MonsterAI.EnterDeath() → Death动画                        │
│    → OnDeathComplete → LootTable.Roll() → OnLootDrop           │
│    → 延迟销毁 → OnDeathComplete → Spawner.HandleDeath           │
└────────────────────────────────────────────────────────────────┘
```

---

## 十二、实现顺序

1. `IDamageable.cs` — 共享接口
2. `MonsterConfig.cs` — ScriptableObject 模板
3. `MonsterLootTable.cs` — 掉落配置
4. `MonsterStats.cs` — 属性管理
5. `MonsterMovement.cs` — NavMeshAgent 移动
6. `MonsterAI.cs` — AI 状态机
7. `MonsterHitZone.cs` + `MonsterAttackHitbox.cs` — 碰撞器
8. `AttackHitbox.cs` + `PlayerHitZone.cs` — 玩家侧碰撞器
9. `MonsterEntity.cs` — 组装入口
10. `MonsterSpawner.cs` — 刷怪管理
11. `MonsterEvents.cs` — 事件定义

---

**文档版本:** 1.0
**最后更新:** 2026-05-06
