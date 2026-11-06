using System;
using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Input;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 联网目标锁定（spec §8.6）——T 键/移动端按钮在 Player/Npc 候选间循环，
    /// 锁定后绑定 `TargetPanel` 与 `SelectionRing`；每帧校验目标死亡/出 AOI/超距并清锁。
    /// 施法前宿主调 <see cref="EnsureTargetFor"/>：无有效锁定时按技能形状选最近候选，
    /// <see cref="LockedTargetId"/> 随 `CastRequest.TargetPlayerId` 上线（服务端显式目标裁决）。
    ///
    /// 由 `ClientNetworkHost` 在联网装配本地角色时 `AddComponent` 并 `Initialize`（单机不创建）；
    /// 宿主每帧显式调用 <see cref="Tick"/>——**刻意不叫 Update**：MonoBehaviour 的 Update 消息会被
    /// Unity 自动调用，与宿主显式驱动叠加会在输入锁存重置的边界顺序下把一次 T 切成两次目标。
    /// 断线/角色重建由宿主调 <see cref="Shutdown"/>，之后 Tick 在 `_registry == null` 处早退。
    /// </summary>
    public sealed class NetworkTargetSelector : MonoBehaviour
    {
        /// <summary>候选获取半径（米）——T/按钮切换与无形状技能的默认范围</summary>
        public const float AcquireRange = 15f;

        /// <summary>锁定保持半径（米）——超出即清锁（15/20 迟滞，避免边界抖动）</summary>
        public const float DropRange = 20f;

        /// <summary>候选快照（纯数据；<see cref="FilterCandidates"/> 可在无场景依赖下单测）</summary>
        public readonly struct Candidate
        {
            public readonly long EntityId;
            public readonly Vector3 Position;
            public readonly byte Kind;
            public readonly bool Dead;
            /// <summary>目标 XZ 身体半径（米；玩家恒 0）——锁定按表面距离与角扩展筛选</summary>
            public readonly float Radius;

            public Candidate(long entityId, Vector3 position, byte kind, bool dead, float radius = 0f)
            {
                EntityId = entityId;
                Position = position;
                Kind = kind;
                Dead = dead;
                Radius = radius;
            }
        }

        private RemoteEntityRegistry? _registry;
        private Transform? _owner;
        private InputManager? _input;
        private SelectionRing? _ring;
        private Action<NetworkTargetable>? _bindPanel;
        private Action? _clearPanel;
        private Func<long>? _localId;
        private long _lockedId;
        private bool _ownerAlive = true;

        /// <summary>当前锁定实体 id；0 = 无锁定（CastRequest 回退服务端自动目标）</summary>
        public long LockedTargetId => _lockedId;

        /// <summary>当前锁定目标的渲染位置；无锁/视图消失/已死亡返回 false。</summary>
        public bool TryGetLockedPosition(out Vector3 position)
        {
            position = default;
            if (_registry == null || _lockedId == 0) return false;
            var view = _registry.GetView(_lockedId);
            if (view == null || view.Dead) return false;
            position = view.Position;
            return true;
        }

        public void Initialize(RemoteEntityRegistry registry, Transform owner,
            InputManager? input, SelectionRing? ring,
            Action<NetworkTargetable>? bindPanel, Action? clearPanel, Func<long>? localId)
        {
            ClearLock();   // 重装配时先解绑上一套面板/选中环
            _registry = registry;
            _owner = owner;
            _input = input;
            _ring = ring;
            _bindPanel = bindPanel;
            _clearPanel = clearPanel;
            _localId = localId;
            _ownerAlive = true;
        }

        /// <summary>断线/本地角色重建：清锁并断开全部引用（组件随角色复用，下次 Initialize 重新接线）</summary>
        public void Shutdown()
        {
            ClearLock();
            _registry = null;
            _owner = null;
            _input = null;
            _ring = null;
            _bindPanel = null;
            _clearPanel = null;
            _localId = null;
            _ownerAlive = true;
        }

        /// <summary>本地玩家存活（服务端死亡/复活事件驱动）：死亡清锁并暂停切换，复活恢复</summary>
        public void SetOwnerAlive(bool alive)
        {
            _ownerAlive = alive;
            if (!alive) ClearLock();
        }

        /// <summary>宿主每帧驱动（见类注释：不是 Unity 的 Update 消息）</summary>
        public void Tick()
        {
            if (_registry == null || _owner == null) return;
            if (_ownerAlive && _input != null && _input.IsTargetSwitchPressed()) Cycle();
            ValidateLocked();
        }

        /// <summary>
        /// 施法前调用（spec §8.6）：
        ///  - 已有锁定 → 用**当前技能的形状/射程**复核（表面距离 ≤ 射程且在前向半角锥内；
        ///    `AOE_Circle` 按 `AreaRadius` 做圆判定，无锥）。不满足 → `ClearLock()` 并**直接返回**，
        ///    `LockedTargetId = 0` 回退服务端自动目标，避免显式目标必空；
        ///  - 无锁定 → 按同一形状选最近候选并绑定（无形状 = `AcquireRange` / ±90°；形状内无候选保持无锁）。
        /// </summary>
        public void EnsureTargetFor(SkillData? data)
        {
            if (_registry == null || _owner == null) return;
            if (!_ownerAlive)
            {
                ClearLock();   // 本地已阵亡：不锁定任何目标（施法应已被状态机拦截）
                return;
            }

            var shape = data != null ? HitShapeConverter.GetShape(data) : null;
            bool isAoeCircle = shape != null && shape.TargetType == TargetType.AOE_Circle;
            float range = isAoeCircle
                ? shape!.AreaRadius
                : (shape != null && shape.Range > 0f ? shape.Range : AcquireRange);
            float halfAngle = shape != null && shape.Angle > 0f ? shape.Angle * 0.5f : 90f;

            if (_lockedId != 0)
            {
                bool stillInShape = IsLockStillValid(out var lockedView)
                                    && IsWithinSkillShape(lockedView!, range, halfAngle, isAoeCircle);
                if (stillInShape) return;

                // 目标在施法瞬间已出当前技能射程/锥：清锁回退 0，不再落到重新选目标
                ClearLock();
                return;
            }

            // AOE 圆用 AreaRadius；半径缺失（0）时该技能没有可用的锁定形状，保持无锁而不是
            // 回退 AcquireRange 去绑定与本次施法无关的目标
            if (isAoeCircle && range <= 0f) return;

            var filtered = FilterCandidates(_owner.position, _owner.forward, BuildCandidates(),
                range, isAoeCircle ? 180f : halfAngle);
            if (filtered.Count == 0) return;
            Bind(filtered[0].EntityId);
        }

        /// <summary>名册晚到：目标面板已绑定当前锁定时重绑（`TargetPanel.Bind` 先 Clear，幂等）</summary>
        public void RefreshTargetPanel()
        {
            if (_lockedId == 0 || _registry == null) return;
            var view = _registry.GetView(_lockedId);
            if (view != null && view.Targetable != null) _bindPanel?.Invoke(view.Targetable);
        }

        /// <summary>清锁并解绑（目标死亡/出 AOI/超距/断线/本地死亡共用；幂等）</summary>
        public void ClearLock()
        {
            if (_lockedId == 0) return;
            _lockedId = 0;
            _ring?.Detach();
            _clearPanel?.Invoke();
        }

        /// <summary>
        /// 候选筛选（纯函数）：仅 `Player` / `Npc`（未知 kind fail-closed），排除死亡、超距、
        /// 前向锥外；按**表面距离升序**（中心距 − 目标半径）。`halfAngleDeg` 是**半角**
        /// （前方 ±halfAngleDeg）；目标半径 &gt; 0 时按 asin(半径/中心距) 扩展半角，
        /// 与服务水平距离/圆-锥判定同口径。forward 只取水平分量（零向量退化为世界 +Z）。
        /// </summary>
        public static List<Candidate> FilterCandidates(Vector3 origin, Vector3 forward,
            IReadOnlyList<Candidate> candidates, float acquireRange, float halfAngleDeg)
        {
            var result = new List<Candidate>();
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float clampedHalf = Mathf.Clamp(halfAngleDeg, 0f, 180f);

            foreach (var candidate in candidates)
            {
                if (candidate.Kind != (byte)EntityKind.Player
                    && candidate.Kind != (byte)EntityKind.Npc) continue;
                if (candidate.Dead) continue;

                Vector3 delta = candidate.Position - origin;
                delta.y = 0f;   // 水平判定：高度差（怪物落地吸附/角色跳跃）不参与射程与锥角
                float centerDist = delta.magnitude;
                float surfaceDist = Mathf.Max(0f, centerDist - candidate.Radius);
                if (surfaceDist > acquireRange) continue;

                // 已在目标身体内 / 零距离退化：角度无意义，直接入选
                if (centerDist <= 0.0001f || centerDist <= candidate.Radius)
                {
                    result.Add(candidate);
                    continue;
                }

                float alphaDeg = Mathf.Asin(Mathf.Clamp(candidate.Radius / centerDist, 0f, 1f))
                                 * Mathf.Rad2Deg;
                float effectiveHalf = Mathf.Min(180f, clampedHalf + alphaDeg);
                float halfCos = Mathf.Cos(effectiveHalf * Mathf.Deg2Rad);
                if (Vector3.Dot(forward, delta / centerDist) < halfCos) continue;
                result.Add(candidate);
            }

            result.Sort((a, b) => SurfaceDistance(origin, a).CompareTo(SurfaceDistance(origin, b)));
            return result;
        }

        /// <summary>候选到角色的表面距离（XZ 平面；已在体内时为 0）。</summary>
        private static float SurfaceDistance(Vector3 origin, Candidate candidate)
        {
            Vector3 delta = candidate.Position - origin;
            delta.y = 0f;
            return Mathf.Max(0f, delta.magnitude - candidate.Radius);
        }

        private void Cycle()
        {
            var filtered = FilterCandidates(_owner!.position, _owner.forward, BuildCandidates(),
                AcquireRange, 90f);
            if (filtered.Count == 0)
            {
                ClearLock();
                return;
            }

            // 当前锁定不在列表（刚出获取范围/已死亡）时 FindIndex = -1 → 取第一个（最近）
            int current = filtered.FindIndex(c => c.EntityId == _lockedId);
            Bind(filtered[(current + 1) % filtered.Count].EntityId);
        }

        private List<Candidate> BuildCandidates()
        {
            var candidates = new List<Candidate>();
            if (_registry == null) return candidates;

            long localId = _localId?.Invoke() ?? 0L;
            foreach (long id in _registry.PlayerIds)
            {
                if (id == localId) continue;             // 排除自己（防御：本地玩家通常不在 AOI 快照里）
                var view = _registry.GetView(id);
                if (view == null) continue;              // 遍历期间被移除
                candidates.Add(new Candidate(id, view.Position, view.Kind, view.Dead, view.BodyRadius));
            }
            return candidates;
        }

        /// <summary>
        /// 当前锁定目标是否落在本次技能的判定形状内（与 <see cref="FilterCandidates"/> 同口径：XZ 平面）。
        /// `omnidirectional` = AOE 圆：只查半径不查前向锥（`AreaRadius` 由调用方传入）。
        /// </summary>
        private bool IsWithinSkillShape(RemoteEntityView view, float range, float halfAngleDeg,
            bool omnidirectional)
        {
            if (range <= 0f || _owner == null) return false;

            Vector3 delta = view.Position - _owner.position;
            delta.y = 0f;
            float centerDist = delta.magnitude;
            float surfaceDist = Mathf.Max(0f, centerDist - view.BodyRadius);
            if (surfaceDist > range) return false;
            if (omnidirectional || centerDist <= 0.0001f || centerDist <= view.BodyRadius) return true;

            Vector3 forward = _owner.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            float alphaDeg = Mathf.Asin(Mathf.Clamp(view.BodyRadius / centerDist, 0f, 1f))
                             * Mathf.Rad2Deg;
            float effectiveHalf = Mathf.Min(180f, Mathf.Clamp(halfAngleDeg, 0f, 180f) + alphaDeg);
            float halfCos = Mathf.Cos(effectiveHalf * Mathf.Deg2Rad);
            return Vector3.Dot(forward, delta / centerDist) >= halfCos;
        }

        private bool IsLockStillValid(out RemoteEntityView? view)
        {
            view = null;
            if (_registry == null || _owner == null || !_ownerAlive || _lockedId == 0) return false;

            view = _registry.GetView(_lockedId);
            if (view == null || view.Dead) return false;

            Vector3 delta = view.Position - _owner.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= DropRange * DropRange;
        }

        private void ValidateLocked()
        {
            if (_lockedId == 0) return;
            if (!IsLockStillValid(out _)) ClearLock();
        }

        private void Bind(long entityId)
        {
            if (_registry == null) return;
            var view = _registry.GetView(entityId);
            if (view == null || view.Targetable == null)
            {
                ClearLock();   // 视图/适配器缺失：不留下"锁了但绑不上"的悬挂 id
                return;
            }

            _lockedId = entityId;
            _ring?.AttachTo(view.GameObject.transform, view.Targetable.SelectionRingYOffset);
            _bindPanel?.Invoke(view.Targetable);
        }

        private void OnDestroy() => ClearLock();
    }
}
