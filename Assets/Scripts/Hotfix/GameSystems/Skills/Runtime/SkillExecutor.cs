using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using UnityEngine;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 技能执行器 - 管理单个技能的完整生命周期
    /// </summary>
    public class SkillExecutor
    {
        public static bool EnableVFX = false;
        private static readonly Collider[] s_HitBuffer = new Collider[32];
        private static readonly Collider[] s_AoeBuffer = new Collider[64];

        private readonly IEffectTarget _owner;
        private readonly SkillData _skillData;
        private readonly SkillStateMachine _stateMachine;
        private readonly SkillInterruptionMatrix _interruptionMatrix;

        // 目标检测
        private Vector3 _targetPosition;
        private Vector3 _attackDirection;
        private System.Collections.Generic.Dictionary<int, int> _consecutiveHits;
        private readonly List<IEffectTarget> _cachedTargets = new List<IEffectTarget>();
        private readonly List<int> _cachedToRemove = new List<int>();
        private readonly List<IEffectTarget> _cachedDamagedTargets = new List<IEffectTarget>();
        private IEffectTarget _targetCharacter;
        private IDashComponent _dashComponent;
        private bool _wasFullCharge;
        private DamageBlock _lastDamageBlock;

        // 回调
        public event Action<int> OnHitboxFrame;              // 判定帧触发
        public event Action OnSkillCompleted;
        public event Action<InterruptionSource> OnSkillInterrupted;
        public event Action<IEffectTarget> OnTargetHit;

        public SkillData Data => _skillData;
        public int SkillId => _skillData.SkillId;
        public SkillSubState CurrentSubState => _stateMachine.CurrentState;
        public bool IsActive => _stateMachine.CurrentState != SkillSubState.Completed &&
                                _stateMachine.CurrentState != SkillSubState.Cancelled &&
                                _stateMachine.CurrentState != SkillSubState.Cooldown;
        public IEffectTarget Owner => _owner;
        public float ElapsedTime => _stateMachine.ElapsedTime;

        public SkillExecutor(
            IEffectTarget owner,
            SkillData data,
            SkillInterruptionMatrix interruptionMatrix = null)
        {
            _owner = owner;
            _skillData = data;
            _interruptionMatrix = interruptionMatrix ?? new SkillInterruptionMatrix();
            _stateMachine = new SkillStateMachine(data);

            _stateMachine.OnHitboxFrame += OnHitboxTriggered;
            _stateMachine.OnHitConfirm += OnHitConfirm;
            _stateMachine.OnSkillCompleted += OnSkillComplete;
            _stateMachine.OnSkillInterrupted += OnSkillInterrupt;
            _stateMachine.OnStateChanged += OnStateChanged;
        }

        /// <summary>
        /// 设置技能目标
        /// </summary>
        public void SetTarget(IEffectTarget target)
        {
            _targetCharacter = target;
            if (target != null)
            {
                _targetPosition = target.transform.position;
            }
        }

        /// <summary>
        /// 设置目标位置
        /// </summary>
        public void SetTargetPosition(Vector3 position)
        {
            _targetPosition = position;
        }

        public void SetAttackDirection(Vector3 direction)
        {
            _attackDirection = direction;
        }

        public void SetDashComponent(IDashComponent dashComponent)
        {
            _dashComponent = dashComponent;
        }

        /// <summary>
        /// 尝试开始释放技能
        /// </summary>
        public bool TryStart()
        {
            bool started = _stateMachine.TryStart();
            if (started && _skillData is ChargedSkillData)
            {
                EventBus.Emit(new SkillChargingStartedEvent { SkillId = _skillData.SkillId });
            }
            return started;
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            _stateMachine.Update(deltaTime);

            if (CurrentSubState == SkillSubState.Charging)
            {
                EventBus.Emit(new SkillChargeTickEvent
                {
                    SkillId = _skillData.SkillId,
                    Progress = GetChargeProgress()
                });
            }
        }

        /// <summary>
        /// 释放蓄力（松开蓄力键）
        /// </summary>
        public void ReleaseCharge()
        {
            if (CurrentSubState == SkillSubState.Charging)
            {
                _wasFullCharge = GetChargeProgress() >= 1f;
                _stateMachine.ReleaseCharge();
            }
        }

        /// <summary>
        /// 尝试中断技能
        /// </summary>
        public bool TryInterrupt(InterruptionSource source)
        {
            if (_interruptionMatrix.CanBeInterruptedInState(_skillData, CurrentSubState, source))
            {
                _stateMachine.Interrupt(source);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 强制完成技能
        /// </summary>
        public void ForceComplete()
        {
            _stateMachine.Complete();
        }

        /// <summary>
        /// 获取蓄力进度 [0, 1]
        /// </summary>
        public float GetChargeProgress()
        {
            if (CurrentSubState != SkillSubState.Charging)
                return CurrentSubState == SkillSubState.Completed ? 1f : 0f;

            var charged = _skillData as ChargedSkillData;
            if (charged == null) return 1f;
            return Mathf.Clamp01(_stateMachine.ElapsedTime / charged.MaxChargeTime);
        }

        /// <summary>
        /// 获取引导进度 [0, 1]
        /// </summary>
        public float GetChannelProgress()
        {
            if (CurrentSubState != SkillSubState.Channeling)
                return CurrentSubState == SkillSubState.Completed ? 1f : 0f;

            var channeled = _skillData as ChanneledSkillData;
            if (channeled == null) return 1f;
            float elapsed = _stateMachine.ElapsedTime - channeled.CastTime;
            return Mathf.Clamp01(elapsed / channeled.ChannelDuration);
        }

        /// <summary>
        /// 获取读条进度 [0, 1]
        /// </summary>
        public float GetCastProgress()
        {
            if (CurrentSubState != SkillSubState.Casting)
                return CurrentSubState == SkillSubState.Completed ? 1f : 0f;

            var channeled = _skillData as ChanneledSkillData;
            float castTime = channeled?.CastTime ?? 0f;
            if (castTime <= 0) return 1f;
            return Mathf.Clamp01(_stateMachine.ElapsedTime / castTime);
        }

        private void OnHitboxTriggered(int frameIndex)
        {
            var targets = DetectTargets();

            // Consecutive hit tracking: only deal damage every 3rd consecutive detection per target
            const int consecutiveRequired = 3;
            if (_consecutiveHits == null)
                _consecutiveHits = new System.Collections.Generic.Dictionary<int, int>();

            // Reset counts for targets no longer detected this tick
            var toRemove = _cachedToRemove;
            toRemove.Clear();
            foreach (var kv in _consecutiveHits)
            {
                bool stillHit = false;
                foreach (var t in targets)
                {
                    if (t.transform.GetInstanceID() == kv.Key) { stillHit = true; break; }
                }
                if (!stillHit) toRemove.Add(kv.Key);
            }
            foreach (var id in toRemove) _consecutiveHits.Remove(id);

            var damagedTargets = _cachedDamagedTargets;
            damagedTargets.Clear();
            foreach (var target in targets)
            {
                int id = target.transform.GetInstanceID();
                _consecutiveHits.TryGetValue(id, out int count);
                count++;
                _consecutiveHits[id] = count;

                if (count >= consecutiveRequired)
                {
                    _consecutiveHits[id] = 0;
                    ApplyDamage(target, frameIndex);
                    ApplyEffects(target);
                    OnTargetHit?.Invoke(target);
                    damagedTargets.Add(target);
                }
            }

            OnHitboxFrame?.Invoke(frameIndex);

            if (damagedTargets.Count > 0)
            {
                var hitPos = damagedTargets[0].transform.position;
                foreach (var t in damagedTargets)
                {
                    EventBus.Emit(new SkillHitTargetEvent
                    {
                        SkillId = _skillData.SkillId,
                        CasterId = _owner.transform.GetInstanceID(),
                        HitPosition = hitPos,
                        IsFullCharge = _wasFullCharge
                    });
                }
            }
        }

        private void OnHitConfirm()
        {
            // 可以在这里触发命中特效、音效等
            PlayHitEffects();
        }

        private void OnSkillComplete()
        {
            _consecutiveHits?.Clear();
            OnSkillCompleted?.Invoke();
        }

        private void OnSkillInterrupt(InterruptionSource source)
        {
            _consecutiveHits?.Clear();
            OnSkillInterrupted?.Invoke(source);
        }

        private void OnStateChanged(SkillSubState newState)
        {
            if (newState == SkillSubState.Execution && _skillData is ChargedSkillData)
            {
                EventBus.Emit(new SkillReleasedEvent
                {
                    SkillId = _skillData.SkillId,
                    IsFullCharge = _wasFullCharge,
                    CasterId = _owner.transform.GetInstanceID()
                });
            }

            if (newState == SkillSubState.Execution &&
                _dashComponent != null &&
                _skillData.DashDistance > 0)
            {
                Vector3 dashDir = _owner.transform.forward;
                _dashComponent.StartDash(dashDir, _skillData.DashDistance, _skillData.DashDuration);
            }
        }

        private ShapeBlock GetShape()
        {
            return (_skillData as ComboSkillData)?.Shape
                ?? (_skillData as InstantSkillData)?.Shape
                ?? (_skillData as ChargedSkillData)?.Shape
                ?? (_skillData as ChanneledSkillData)?.Shape;
        }

        private List<IEffectTarget> DetectTargets()
        {
            var targets = _cachedTargets;
            targets.Clear();
            ShapeBlock shape = GetShape();
            if (shape == null) return targets;

            if (shape.AreaRadius > 0)
                DetectAOETargets(targets, shape);
            else
                DetectMeleeSector(targets, shape);

            return targets;
        }

        private void DetectMeleeSector(List<IEffectTarget> targets, ShapeBlock shape)
        {
            if (_targetCharacter != null && _targetCharacter != _owner)
            {
                float distance = Vector3.Distance(_owner.transform.position, _targetCharacter.transform.position);
                if (distance <= shape.Range)
                    targets.Add(_targetCharacter);
                return;
            }

            Vector3 origin = _owner.transform.position;
            Vector3 forward;
            if (_attackDirection.sqrMagnitude > 0.0001f)
                forward = _attackDirection.normalized;
            else if (_targetPosition.sqrMagnitude > 0.0001f)
                forward = (_targetPosition - origin).normalized;
            else
                forward = _owner.transform.forward;
            float halfInnerAngle = shape.InnerAngle * 0.5f;

            int count = Physics.OverlapSphereNonAlloc(origin, shape.Range, s_HitBuffer, shape.TargetMask);
            for (int i = 0; i < count; i++)
            {
                var col = s_HitBuffer[i];
                if (col.transform.IsChildOf(_owner.transform)) continue;

                Vector3 dir;

                bool useClosestPoint = col is BoxCollider
                                    || col is SphereCollider
                                    || col is CapsuleCollider
                                    || (col is MeshCollider mc && mc.convex);

                if (useClosestPoint)
                {
                    Vector3 closest = col.ClosestPoint(origin);
                    dir = closest - origin;

                    // Fallback when player origin is inside the collider (ClosestPoint returns origin)
                    if (dir.sqrMagnitude < 0.0001f)
                        dir = col.transform.position - origin;
                }
                else
                {
                    // Non-convex MeshCollider, TerrainCollider, etc.
                    // Use bounds center (closer to actual overlap than transform.position)
                    dir = col.bounds.center - origin;
                }

                float dist = dir.magnitude;
                float angle = Vector3.SignedAngle(forward, dir, Vector3.up);

                // Inner hit zone: bypass sector check when within inner radius and inner angle
                bool inInnerZone = shape.InnerRadius > 0f
                                   && dist <= shape.InnerRadius
                                   && Mathf.Abs(angle) <= halfInnerAngle;

                if (!inInnerZone)
                {
                    if (angle < shape.AngleStart || angle > shape.AngleEnd)
                        continue;
                }

                var target = col.GetComponentInParent<IEffectTarget>();
                if (target == null || target == _owner || targets.Contains(target))
                    continue;
                targets.Add(target);
            }
        }

        private void DetectAOETargets(List<IEffectTarget> targets, ShapeBlock shape)
        {
            // When no target is locked, center the AOE on the owner (for self-centered skills like spin attacks).
            // When a target IS locked, center on the target.
            Vector3 center = _targetCharacter != null
                ? _targetCharacter.transform.position
                : _owner.transform.position;

            if (shape.TargetType == TargetType.AOE_Cone)
                DetectConeTargets(center, targets, shape);
            else
            {
                int count = Physics.OverlapSphereNonAlloc(center, shape.AreaRadius, s_AoeBuffer, shape.TargetMask);
                for (int i = 0; i < count; i++)
                {
                    if (s_AoeBuffer[i].TryGetComponent(out IEffectTarget target) && target != _owner)
                        targets.Add(target);
                }
            }
        }

        private void DetectConeTargets(Vector3 center, List<IEffectTarget> targets, ShapeBlock shape)
        {
            Vector3 ownerPos = _owner.transform.position;
            Vector3 directionToCenter = (center - ownerPos).normalized;
            float halfAngle = shape.Angle / 2f;
            int count = Physics.OverlapSphereNonAlloc(ownerPos, shape.Range, s_AoeBuffer, shape.TargetMask);
            for (int i = 0; i < count; i++)
            {
                if (s_AoeBuffer[i].TryGetComponent(out IEffectTarget target) && target != _owner)
                {
                    Vector3 dirToTarget = (target.transform.position - ownerPos).normalized;
                    float angle = Vector3.Angle(directionToCenter, dirToTarget);
                    if (angle <= halfAngle)
                        targets.Add(target);
                }
            }
        }

        private void ApplyDamage(IEffectTarget target, int frameIndex)
        {
            var damageBlock = _skillData.Damage;
            if (damageBlock == null) return;

            float damage = damageBlock.CalculateFinalDamage(_owner.Stats);
            if (CurrentSubState == SkillSubState.Charging || CurrentSubState == SkillSubState.Execution)
                damage *= 1f + GetChargeProgress() * 0.5f;

            _lastDamageBlock = damageBlock;

            // Set runtime skill context so MonsterEntity.TakeDamage can emit
            // MonsterTakeDamageEvent with correct SkillId/ComboIndex
            damageBlock.SkillId = _skillData.SkillId;
            damageBlock.ComboIndex = frameIndex + 1;

            // Set knockback force from EffectBlock if present (overrides DamageBlock default)
            var effect = GetEffect();
            if (effect != null && effect.KnockbackForce > 0)
                damageBlock.KnockbackForce = effect.KnockbackForce;

            // Route through IDamageable for unified feedback path
            if (target is IDamageable damageable)
            {
                Vector3 hitDir = (target.transform.position - _owner.transform.position).normalized;
                damageable.TakeDamage(damageBlock, hitDir);
            }
            else
            {
                target.Heal(-damage);
            }
        }

        private EffectBlock GetEffect()
        {
            return (_skillData as InstantSkillData)?.Effect
                ?? (_skillData as ChargedSkillData)?.Effect
                ?? (_skillData as ChanneledSkillData)?.Effect
                ?? (_skillData as ProjectileSkillData)?.Effect;
        }

        private void ApplyEffects(IEffectTarget target)
        {
            var effect = GetEffect();
            if (effect?.ApplyEffects == null) return;
            foreach (var effectData in effect.ApplyEffects)
                effectData?.Apply(_owner, target);
        }

        // Pooled instance of the release VFX. Re-instantiated only if destroyed.
        private GameObject _cachedReleaseVFX;

        private void PlayHitEffects()
        {
            if (!EnableVFX) return;
            PresentationBlock pres = GetPresentation();
            if (pres?.ReleaseVFX == null) return;

            // Reuse cached instance instead of Instantiate every hit
            if (_cachedReleaseVFX == null)
            {
                _cachedReleaseVFX = UnityEngine.Object.Instantiate(pres.ReleaseVFX);
                _cachedReleaseVFX.name = pres.ReleaseVFX.name + "_Pooled";
            }
            _cachedReleaseVFX.transform.position = _targetPosition;
            _cachedReleaseVFX.transform.rotation = Quaternion.identity;

            var ps = _cachedReleaseVFX.GetComponent<ParticleSystem>();
            if (ps != null) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(); }
        }

        private PresentationBlock GetPresentation()
        {
            return (_skillData as ComboSkillData)?.Presentation
                ?? (_skillData as InstantSkillData)?.Presentation
                ?? (_skillData as ChargedSkillData)?.Presentation
                ?? (_skillData as ChanneledSkillData)?.Presentation
                ?? (_skillData as ProjectileSkillData)?.Presentation;
        }
    }
}
