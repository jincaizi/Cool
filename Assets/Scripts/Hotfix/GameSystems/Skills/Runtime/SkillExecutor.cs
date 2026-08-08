using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using UnityEngine;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
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
        private float _chargeProgressAtRelease;
        private DamageBlock _lastDamageBlock;
        private readonly SpinSkillData _spinData;
        private SpinHitTracker _spinHitTracker;
        private bool _swordQiSpawned;

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
            _spinData = data as SpinSkillData;
            _stateMachine = new SkillStateMachine(data);

            _stateMachine.OnHitboxFrame += OnHitboxTriggered;
            _stateMachine.OnHitboxFrame += OnSwordQiFrame;
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
            _swordQiSpawned = false; // 每次施放复位（连击链多次施放）
            _chargeProgressAtRelease = 0f; // 防御性复位（防未来直接进入 Execution 的路径读到旧值）
            bool started = _stateMachine.TryStart();
            if (started && _skillData is ChargedSkillData)
            {
                EventBus.Emit(new SkillChargingStartedEvent { SkillId = _skillData.SkillId });
            }
            return started;
        }

        /// <summary>
        /// 旋转技能：当前是否在可取消窗口内
        /// </summary>
        public bool CanCancel()
        {
            return _stateMachine.CanCancel();
        }

        /// <summary>
        /// 旋转技能：窗口内主动取消（正常完成语义）
        /// </summary>
        public bool Cancel()
        {
            return _stateMachine.Cancel();
        }

        /// <summary>
        /// 蓄力技能：松开按钮提前释放（转发到状态机）
        /// </summary>
        public bool Release()
        {
            return _stateMachine.Release();
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
        /// 蓄力伤害倍率：按释放进度查数据曲线（1.0 → 曲线末端值）
        /// </summary>
        private float GetDamageScaleForCharge(float progress)
        {
            if (!(_skillData is ChargedSkillData charged)) return 1f;
            return charged.GetDamageScaleForCharge(progress);
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

        /// <summary>
        /// 判定帧事件第二订阅者：按 SwordQiConfig 生成剑气（挥击中段，与近战判定同帧）。
        /// 伤害数据从 SkillData.Damage 拷贝并填充上下文，防共享实例被后续施放污染。
        /// </summary>
        private void OnSwordQiFrame(int frameIndex)
        {
            if (_swordQiSpawned) return;

            var cfg = _skillData.SwordQi;
            if (cfg == null || cfg.Prefab == null) return;
            if (frameIndex != cfg.SpawnFrameIndex) return;

            _swordQiSpawned = true;

            var damage = _skillData.Damage?.Clone();
            if (damage == null)
            {
                Debug.LogWarning("[SkillExecutor] SwordQi config set but skill has no DamageBlock, skip spawn");
                return;
            }

            SkillDamageHelper.FillDamageContext(damage, _skillData.SkillId, frameIndex + 1, cfg.Effect, _owner.Stats);
            damage.CalculatedDamage *= cfg.DamagePercent;

            Vector3 origin = _owner.transform.position + _owner.transform.TransformDirection(cfg.SpawnOffset);
            Vector3 dir = _owner.transform.forward;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : _owner.transform.forward;

            SwordQiSpawner.Spawn(cfg, damage, origin, dir);

            EventBus.Emit(new SwordQiSpawnedEvent
            {
                CasterId = _owner.transform.GetInstanceID(),
                SkillId = _skillData.SkillId,
                Origin = origin,
                Direction = dir,
                Speed = cfg.Speed,
                Range = cfg.Range
            });
        }

        private void OnHitboxTriggered(int frameIndex)
        {
            var targets = DetectTargets();

            // 蓄力期判定帧不结算伤害（ChargeBlast 爆炸只在 Execution 判定帧；蓄力期帧事件由 OnSwordQiFrame/OnHitConfirm 链路消费）
            if (CurrentSubState == SkillSubState.Charging)
                return;

            if (_spinData != null)
            {
                HandleSpinTick(targets, frameIndex);
                return;
            }
            // 以下为现有非旋转逻辑（保持不变）
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

        private void HandleSpinTick(List<IEffectTarget> targets, int tickIndex)
        {
            if (_spinHitTracker == null)
                _spinHitTracker = new SpinHitTracker(_spinData.MaxHitsPerTarget);

            var damagedTargets = _cachedDamagedTargets;
            damagedTargets.Clear();
            foreach (var target in targets)
            {
                if (target is IDamageable damageable && !damageable.IsAlive)
                    continue;
                int id = target.transform.GetInstanceID();
                if (!_spinHitTracker.TryRecordHit(id)) continue;
                ApplyDamage(target, tickIndex);
                ApplyEffects(target);
                OnTargetHit?.Invoke(target);
                damagedTargets.Add(target);
            }

            OnHitboxFrame?.Invoke(tickIndex);

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

            // 旋转tick进度事件（VFX 复用蓄力事件，Progress 供剑光/拖尾强度）
            EventBus.Emit(new SkillChargeTickEvent
            {
                SkillId = _skillData.SkillId,
                Progress = Mathf.Clamp01(_stateMachine.ElapsedTime / _spinData.MaxDuration)
            });
        }

        private void OnHitConfirm()
        {
            // 可以在这里触发命中特效、音效等
            PlayHitEffects();
        }

        private void OnSkillComplete()
        {
            if (_spinData != null)
            {
                EventBus.Emit(new SkillReleasedEvent
                {
                    SkillId = _skillData.SkillId,
                    IsFullCharge = false,
                    CasterId = _owner.transform.GetInstanceID()
                });
                _spinHitTracker?.Clear();
            }
            _consecutiveHits?.Clear();
            OnSkillCompleted?.Invoke();
        }

        private void OnSkillInterrupt(InterruptionSource source)
        {
            if (_spinData != null)
            {
                EventBus.Emit(new SkillReleasedEvent
                {
                    SkillId = _skillData.SkillId,
                    IsFullCharge = false,
                    CasterId = _owner.transform.GetInstanceID()
                });
                _spinHitTracker?.Clear();
            }
            else if (_skillData is ChargedSkillData)
            {
                // 蓄力中被打断：补发释放事件，武器 VFX 才能熄灭
                EventBus.Emit(new SkillReleasedEvent
                {
                    SkillId = _skillData.SkillId,
                    IsFullCharge = false,
                    CasterId = _owner.transform.GetInstanceID()
                });
            }
            _consecutiveHits?.Clear();
            OnSkillInterrupted?.Invoke(source);
        }

        private void OnStateChanged(SkillSubState newState)
        {
            if (newState == SkillSubState.Spinning && _spinData != null)
            {
                EventBus.Emit(new SkillChargingStartedEvent { SkillId = _skillData.SkillId });
            }

            if (newState == SkillSubState.Execution && _skillData is ChargedSkillData)
            {
                // 记录释放瞬间进度与满蓄力标记（判定帧结算与 SkillHitTargetEvent 使用）
                _chargeProgressAtRelease = _stateMachine.ChargeProgressAtRelease;
                _wasFullCharge = _chargeProgressAtRelease >= 1f;
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
                ?? (_skillData as ChanneledSkillData)?.Shape
                ?? (_skillData as SpinSkillData)?.Shape;
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

            // 蓄力技能：Execution 阶段按释放进度放大 AOE 半径
            float radius = shape.AreaRadius;
            if (_skillData is ChargedSkillData && CurrentSubState == SkillSubState.Execution)
            {
                float areaScale = ((ChargedSkillData)_skillData).GetAreaScaleForCharge(_chargeProgressAtRelease);
                radius = shape.AreaRadius * areaScale;
            }

            if (shape.TargetType == TargetType.AOE_Cone)
                DetectConeTargets(center, targets, shape);
            else
            {
                int count = Physics.OverlapSphereNonAlloc(center, radius, s_AoeBuffer, shape.TargetMask);
                for (int i = 0; i < count; i++)
                {
                    if (s_AoeBuffer[i].TryGetComponent(out IEffectTarget target) && target != _owner && !targets.Contains(target))
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
                    if (angle <= halfAngle && !targets.Contains(target))
                        targets.Add(target);
                }
            }
        }

        private void ApplyDamage(IEffectTarget target, int frameIndex)
        {
            var damageBlock = _skillData.Damage;
            if (damageBlock == null) return;

            SkillDamageHelper.FillDamageContext(damageBlock, _skillData.SkillId, frameIndex + 1,
                                                GetEffect(), _owner.Stats);

            // 蓄力技能：释放瞬间按实际进度 × 曲线结算（修复原硬编码 1.5x 且 Execution 态取 0 的 bug）
            if (_skillData is ChargedSkillData)
                damageBlock.CalculatedDamage *= GetDamageScaleForCharge(_chargeProgressAtRelease);

            _lastDamageBlock = damageBlock;

            // Route through IDamageable for unified feedback path
            if (target is IDamageable damageable)
            {
                Vector3 hitDir = (target.transform.position - _owner.transform.position).normalized;
                damageable.TakeDamage(damageBlock, hitDir);
            }
            else
            {
                target.Heal(-damageBlock.CalculatedDamage);
            }
        }

        private EffectBlock GetEffect()
        {
            return (_skillData as InstantSkillData)?.Effect
                ?? (_skillData as ChargedSkillData)?.Effect
                ?? (_skillData as ChanneledSkillData)?.Effect
                ?? (_skillData as ProjectileSkillData)?.Effect
                ?? (_skillData as SpinSkillData)?.Effect;
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
                ?? (_skillData as ProjectileSkillData)?.Presentation
                ?? (_skillData as SpinSkillData)?.Presentation;
        }
    }
}
