using System;
using System.Collections.Generic;

namespace Cool.SyncCore
{
    public enum StatusEffectKind { Slow = 1, Burn = 2, Paralyze = 3, Vulnerable = 4 }
    public enum StatusApplyResult { Ignored, Applied, Refreshed }

    public sealed class ActiveStatus
    {
        public StatusEffectDefinition Definition = null!;
        public long SourceId;
        public int SourceSkillId;
        public ulong SourceCastId;
        public uint AppliedTick;
        public uint EndTick;
        public uint NextTick;
        public bool PendingRemoval;
        public bool IsPvpControl;

        public int EffectId => Definition.EffectId;
        public int GroupId => Definition.StackGroupId;
        public StatusEffectKind Kind => (StatusEffectKind)Definition.EffectKind;
        public bool IsDot => Kind == StatusEffectKind.Burn;
    }

    /// <summary>状态运行时：纯逻辑，服务端与客户端离线共用；tick 由调用方按 15Hz 提供。</summary>
    public sealed class StatusRuntime
    {
        private readonly Dictionary<int, ActiveStatus> _byEffect = new();
        private readonly Dictionary<int, ActiveStatus> _byGroup = new();
        private uint _controlImmunityTicks;
        private uint _vulnerabilityNoRefreshTicks;
        private uint? _lastVulnerableAppliedTick;

        public IEnumerable<ActiveStatus> Active => _byEffect.Values;
        public int Count => _byEffect.Count;
        public uint ControlImmuneUntilTick { get; private set; }

        public void Configure(PvpCombatSettings pvp)
        {
            _controlImmunityTicks = SecondsToTicks(pvp.ControlImmunitySeconds);
            _vulnerabilityNoRefreshTicks = SecondsToTicks(pvp.SameGroupVulnerabilityNoRefreshSeconds);
        }

        public StatusApplyResult Apply(StatusEffectDefinition def, long sourceId, int skillId,
            ulong castId, uint tick, bool targetIsPlayer, bool sourceIsPlayer)
        {
            if (def == null) return StatusApplyResult.Ignored;
            var kind = (StatusEffectKind)def.EffectKind;
            uint duration = SecondsToTicks(def.DurationSeconds);
            if (duration == 0) return StatusApplyResult.Ignored;

            if (kind == StatusEffectKind.Paralyze && tick < ControlImmuneUntilTick)
                return StatusApplyResult.Ignored;
            // 4s 窗口跨状态移除保存：不能只看 _byGroup 里是否仍有活跃 wind。
            if (kind == StatusEffectKind.Vulnerable && targetIsPlayer && sourceIsPlayer
                && _lastVulnerableAppliedTick.HasValue
                && tick - _lastVulnerableAppliedTick.Value < _vulnerabilityNoRefreshTicks)
                return StatusApplyResult.Ignored;

            if (_byGroup.TryGetValue(def.StackGroupId, out var existing))
            {
                if (existing.EffectId != def.EffectId)
                {
                    Remove(existing.EffectId, tick);
                }
                else
                {
                    existing.Definition = def;
                    existing.EndTick = AddTicks(tick, duration);
                    existing.PendingRemoval = false;
                    existing.IsPvpControl = kind == StatusEffectKind.Paralyze
                        && targetIsPlayer && sourceIsPlayer;
                    if (existing.IsDot)
                    {
                        uint next = AddTicks(tick, SecondsToTicks(def.IntervalSeconds));
                        existing.NextTick = existing.NextTick == 0 || next < existing.NextTick
                            ? next : existing.NextTick; // 保留首次来源
                    }
                    else
                    {
                        existing.SourceId = sourceId;
                        existing.SourceSkillId = skillId;
                        existing.SourceCastId = castId;
                        existing.AppliedTick = tick;
                    }
                    if (kind == StatusEffectKind.Vulnerable) _lastVulnerableAppliedTick = tick;
                    return StatusApplyResult.Refreshed;
                }
            }

            var status = new ActiveStatus
            {
                Definition = def, SourceId = sourceId, SourceSkillId = skillId, SourceCastId = castId,
                AppliedTick = tick, EndTick = AddTicks(tick, duration),
                NextTick = kind == StatusEffectKind.Burn
                    ? AddTicks(tick, SecondsToTicks(def.IntervalSeconds)) : 0u,
                IsPvpControl = kind == StatusEffectKind.Paralyze && targetIsPlayer && sourceIsPlayer
            };
            _byEffect[def.EffectId] = status;
            _byGroup[def.StackGroupId] = status;
            if (kind == StatusEffectKind.Vulnerable) _lastVulnerableAppliedTick = tick;
            return StatusApplyResult.Applied;
        }

        public bool Remove(int effectId, uint tick)
        {
            if (!_byEffect.Remove(effectId, out var status)) return false;
            if (status.IsPvpControl)
                ControlImmuneUntilTick = AddTicks(tick, _controlImmunityTicks);
            if (_byGroup.TryGetValue(status.GroupId, out var group) && ReferenceEquals(group, status))
                _byGroup.Remove(status.GroupId);
            return true;
        }

        public int RemoveExpiredModifiers(uint tick)
        {
            var expired = new List<int>();
            foreach (var s in _byEffect.Values)
                if (!s.IsDot && tick >= s.EndTick) expired.Add(s.EffectId);
            foreach (var id in expired) Remove(id, tick);
            return expired.Count;
        }

        public void CollectDueDots(uint tick, List<ActiveStatus> due)
        {
            foreach (var s in _byEffect.Values)
            {
                if (!s.IsDot || s.NextTick == 0 || tick < s.NextTick) continue;
                due.Add(s);
                s.PendingRemoval = tick >= s.EndTick;
            }
        }

        /// <summary>没有排定跳落在 EndTick 时（刷新错相/间隔不整除）到期移除；必须在 CollectDueDots/AdvanceDot 之后调用。</summary>
        public int RemoveExpiredDots(uint tick)
        {
            var expired = new List<int>();
            foreach (var s in _byEffect.Values)
                if (s.IsDot && tick >= s.EndTick) expired.Add(s.EffectId);
            foreach (var id in expired) Remove(id, tick);
            return expired.Count;
        }

        public void AdvanceDot(ActiveStatus status, uint tick)
        {
            if (status.PendingRemoval || tick >= status.EndTick)
            {
                Remove(status.EffectId, tick);
                return;
            }
            status.NextTick = AddTicks(status.NextTick, SecondsToTicks(status.Definition.IntervalSeconds));
        }

        public void DeriveControl(out float speedScale, out bool actionLock)
        {
            float slow = 0f;
            actionLock = false;
            foreach (var s in _byEffect.Values)
            {
                if (s.Kind == StatusEffectKind.Slow) slow = Math.Max(slow, s.Definition.SlowRatio);
                else if (s.Kind == StatusEffectKind.Paralyze) actionLock = true;
            }
            speedScale = Math.Clamp(1f - slow, 0f, 1f);
        }

        public float IncomingDamageMultiplier(bool pvp, float pvpMultiplier)
        {
            float result = 1f;
            foreach (var s in _byEffect.Values)
                if (s.Kind == StatusEffectKind.Vulnerable)
                    result *= pvp ? pvpMultiplier : s.Definition.IncomingDamageMultiplier;
            return result;
        }

        public void ClearAll()
        {
            _byEffect.Clear();
            _byGroup.Clear();
            ControlImmuneUntilTick = 0;
            _lastVulnerableAppliedTick = null;
        }

        public static uint SecondsToTicks(float seconds)
            => seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)
                ? 0u : (uint)Math.Ceiling(seconds * CombatRules.TickRateHz);

        private static uint AddTicks(uint tick, uint delta)
            => delta > uint.MaxValue - tick ? uint.MaxValue : tick + delta;
    }
}
