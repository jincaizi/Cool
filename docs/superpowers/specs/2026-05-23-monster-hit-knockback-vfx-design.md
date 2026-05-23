# Monster Hit Knockback & VFX Design

## Overview

Add combat feel to monster hit reactions: variable knockback based on attack type, hit particles with object pooling, and death knockback. Extend existing `MonsterAI` / `MonsterMovement` with minimal new components.

## 1. Knockback System

### MonsterMovement Changes

Add knockback state to the movement class:

```
_knockbackVelocity: Vector3
_knockbackTimer: float

+ ApplyKnockback(Vector3 direction, float force):
    _knockbackVelocity = direction * force
    _knockbackTimer = config.KnockbackDecay  // default 0.5s

+ GetKnockbackDisplacement(): Vector3
    return _knockbackVelocity * deltaTime

+ UpdateKnockback(float deltaTime):
    if _knockbackTimer <= 0: return
    _knockbackTimer -= deltaTime
    _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, decayRate * deltaTime)
    transform.position += _knockbackVelocity * deltaTime
```

Knockback is applied while NavMeshAgent remains stopped (existing behavior in `NotifyHit`). On recovery, `Resume()` reactivates the agent from the new position.

### MonsterConfig Addition

```
+ KnockbackDecay: float = 0.5f   // time to fully decay knockback
+ DeathKnockbackMultiplier: float = 1.5f
```

### MonsterAI Hit Flow

```
NotifyHit(damage, hitDirection, knockbackForce):
    _preHitState = currentState   // already exists
    _movement.ApplyKnockback(hitDirection, knockbackForce)
    _movement.Stop()              // already exists, stops NavMeshAgent
    _stateTimer = knockbackDecay + hitAnimDuration
    TransitionTo(Hit)

ExecuteState(Hit) each frame:
    _movement.UpdateKnockback(deltaTime)

RecoverFromHit():
    _movement.Resume()            // re-enables NavMeshAgent, auto-navigates
    → Chase/Attack or fallback to _preHitState
```

### Death Knockback

```
HandleDeath():
    _movement.ApplyKnockback(lastHitDirection, knockbackForce * DeathKnockbackMultiplier)
    _movement.Stop()
    → TransitionTo(Death)
    → In Death update loop: keep updating knockback until velocity zero
    → NavMeshAgent.enabled = false (already exists)
    → Play death animation after knockback settles
```

### Edge Cases

- **Hit again during knockback**: `NotifyHit` overwrites knockback velocity with new direction, timer resets
- **Hit during death knockback**: death has highest priority, death knockback continues uninterrupted
- **Zero knockback force (knockbackForce = 0)**: skip ApplyKnockback entirely, still play hit animation + flash + particles, timer = hitAnimDuration only
- **Knockback into walls/obstacles**: NavMeshAgent auto-repositions on Resume() via Warp/SetDestination
- **Knockback off NavMesh**: NavMeshAgent.SamplePosition auto-finds nearest valid position on Resume()
- **Knockback during Patrol/Idle**: _preHitState saved correctly, recover to Idle → auto-resumes patrol via existing EvaluateTransitions
- **Knockback during Defend**: if hit from front within defend angle → defend absorbs (existing behavior, no knockback). If hit from behind → full knockback, interrupts defend
- **Knockback during Taunt**: taunt is interrupted, monster enters hit → recovers to chase/attack (if target exists)
- **Monster at spawn boundary**: knockback distance is naturally capped by decay time and force. No artificial leash needed — the agent navigates back on recovery
- **Death during patrol/idle (no target)**: lastHitDirection defaults to monster's -forward direction so death knockback flies backward
- **Multiple monsters hit simultaneously**: each monster's hit is independent, shared particle pool handles concurrent Get() calls — pool grows as needed
- **Particle prefab not assigned in HitParticleController**: log warning once, skip spawning. Don't block hit flow
- **Scene unload during particle playback**: OnDestroy in HitParticleController clears pool, orphaned ParticleSystem instances get cleaned up by Unity scene unload
- **Monster destroyed during hit animation**: OnDestroy cancels any pending knockback, particle pool survives (static/shared)

## 2. Object Pool

### ComponentPool<T> where T : Component

Generic reusable pool, created in `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Pool/`:

```
- _pool: Queue<T>
- _prefab: T
- _parent: Transform

+ Get(): T
    if pool empty → Instantiate(_prefab, _parent)
    else → Dequeue, SetActive(true)
+ Return(T instance): SetActive(false), Enqueue
+ Prewarm(int count)
```

No max-size cap on Get() — pool grows as needed. Return is driven by consumer (ParticleSystem.Stop callback). Pool is shared (static) across all HitParticleController instances — one pool per particle prefab type.

## 3. Hit Particles

### HitParticleController

New MonoBehaviour, attached to monster prefab:

```
[SerializeField] _normalHitParticles: GameObject   // prefab with ParticleSystem + StopAction=Callback
[SerializeField] _criticalHitParticles: GameObject  // optional, falls back to normal

OnEnable → EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged)
OnDisable → unsubscribe

OnMonsterDamaged(e):
    prefab = e.IsCritical && _criticalHitParticles ? _criticalHitParticles : _normalHitParticles
    ps = ComponentPool<ParticleSystem>.Get()
    ps.transform.position = e.HitPosition
    ps.Play()
    // OnParticleSystemStopped → pool.Return(ps)
```

### Prefab Setup

- Each particle prefab has `ParticleSystem` with `StopAction = Callback`
- `OnParticleSystemStopped` callback triggers `pool.Return()`
- Main module: short lifetime (0.3-0.5s), burst emission

## 4. Hit Flash (Existing)

`HitFlashVFX` already listens to `MonsterTakeDamageEvent` and applies outline flash. Ensure it's added to monster prefabs. No code changes needed.

## Files Changed

| File | Change |
|------|--------|
| `MonsterMovement.cs` | Add knockback velocity, ApplyKnockback, UpdateKnockback, GetKnockbackDisplacement |
| `MonsterAI.cs` | Wire knockback into NotifyHit, ExecuteState(Hit), HandleDeath |
| `MonsterConfig.cs` | Add KnockbackDecay, DeathKnockbackMultiplier |
| `ComponentPool.cs` (new) | Generic object pool |
| `HitParticleController.cs` (new) | Spawn hit particles via pool |

## Acceptance Criteria

### AC-1: 普通受击 — 击退 + 视觉
1. 玩家攻击命中怪物
2. 怪物播放受击动画（已有），同时向后微击退（0.2-0.3m），身上闪白，命中点爆出粒子
3. 击退结束后怪物恢复行为（追击/巡逻）
4. 验证：Unity Editor 运行场景，攻击怪物，观察位移和粒子

### AC-2: 重击加大击退
1. 使用技能/重攻击命中怪物（knockbackForce 更大）
2. 击退距离明显大于普通攻击（0.5-1.5m）
3. 恢复后行为正确
4. 验证：对比普通攻击和技能攻击的击退距离

### AC-3: 死亡击飞
1. 怪物 HP 降到 0
2. 怪物沿受击方向飞出（距离为普通击退的 1.5x），速度衰减到 0
3. 击飞停止后播放死亡动画，延迟后销毁
4. 验证：击杀怪物，观察死亡飞出的轨迹

### AC-4: 高频攻击无 GC 压力
1. 连续快速攻击同一/多个怪物（至少 30 次命中的高频场景）
2. 粒子池不会无限增长，无 GC Alloc 每帧警告
3. 验证：Unity Profiler → GC Alloc 指标，命中期间不应出现粒子相关的 GC.Alloc 尖峰

### AC-5: 击退撞墙
1. 怪物靠近墙壁时受击
2. 击退位移被墙挡住，怪物停在墙边
3. 恢复后 NavMeshAgent 正确从墙边位置重新导航
4. 验证：把怪物引到墙边攻击

### AC-6: 攻防交互（Defend 状态下受击）
1. 怪物处于 Defend 状态时，从正面攻击 → 格挡成功，无击退，伤害减免（已有逻辑）
2. 从背面/侧面攻击 → 正常击退，打断 Defend
3. 验证：等怪物进入防御状态后从不同方向攻击

### AC-7: 无击退力时仍正常反馈
1. 攻击数据 knockbackForce = 0
2. 怪物仍播放受击动画 + 闪白 + 粒子，仅无位移
3. 验证：修改临时测试攻击数据的 knockbackForce 为 0

### AC-8: 怪物无目标时死亡
1. 无玩家仇恨时怪物被杀死（如调试命令/环境伤害）
2. 死亡击退方向用自身的 -forward
3. 验证：用 /kill 类命令杀掉空闲怪物

### AC-9: 粒子池回收
1. 命中粒子播放完成后自动回池
2. 同一帧多次命中，池中取多个实例，播放完毕后全部回收
3. 验证：连续攻击后检查粒子池 Queue 中的实例数稳定在一合理值
