using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
namespace KcpServer.Monster
{
    /// <summary>Main-thread owner. Current and history are detached values; pending casts carry only instance tokens.</summary>
    public sealed class MonsterActionRunner
    {
        private readonly MonsterState _owner;
        private readonly EntityStore _store;
        private readonly NpcCatalogDefinition _catalog;
        private NpcActionSnapshot _current = new NpcActionSnapshot();
        private NpcSkillDefinition? _skill;
        private int _committedStages;
        private ulong _nextInstance;
        private bool _armor;
        private uint? _lastReaction;
        private readonly List<(uint Tick, NpcActionSnapshot State)> _history = new();
        public MonsterActionRunner(MonsterState owner, EntityStore store, NpcCatalogDefinition catalog)
        { _owner = owner; _store = store; _catalog = catalog; SetContinuous(false, 0); }
        public NpcActionSnapshot Current => Copy(_current);
        public bool CanStartSkill => _current.Kind != NpcActionKind.Death &&
            (_current.Kind == NpcActionKind.Idle || _current.Kind == NpcActionKind.Move);
        public NpcSkillDefinition? FindSkill(int id) => _catalog.Skills.Find(s => s.SkillId == id);
        public string? Validate(int skillId, long targetId, uint tick)
        {
            if (!CanStartSkill) return "npc-action-busy";
            var skill = FindSkill(skillId);
            if (skill == null) return "npc-skill-missing";
            var self = _store.GetPlayer(_owner.NpcId);
            var target = _store.GetPlayer(targetId);
            if (self == null || !self.IsAlive) return "caster-dead";
            if (!ValidTarget(targetId)) return "npc-target-invalid";
            var binding = _catalog.Templates.Find(t => t.TemplateId == _owner.TemplateId)?.Skills.Find(s => s.SkillId == skillId);
            if (binding == null) return "npc-skill-unbound";
            float hp = self.Hp / self.MaxHp;
            float d = Distance(self.Position,target!.Position);
            if (hp < binding.MinHpRatio || hp > binding.MaxHpRatio) return "npc-hp-condition";
            if (d < binding.MinRange || d > binding.MaxRange) return "npc-range";
            if (!self.IsCastReady(skillId,tick,skill.CooldownTicks)) return "cooldown";
            if (_owner.Follower != null && !_owner.Follower.HasAttackLineOfSight(self.Position,target.Position)) return "npc-los";
            return null;
        }
        public bool TryBeginSkill(int skillId, long targetId, uint tick)
        {
            if (Validate(skillId,targetId,tick) != null) return false;
            _skill = FindSkill(skillId)!; _committedStages = 0;
            var template = _catalog.Templates.Find(t => t.TemplateId == _owner.TemplateId)!;
            _armor = _skill.ArmorOverride == NpcWindupArmorOverride.Armor || (_skill.ArmorOverride == NpcWindupArmorOverride.Inherit && template.Ai.DefaultWindupArmor);
            var target = _store.GetPlayer(targetId)!;
            uint active = tick + _skill.WindupTicks;
            uint recovery = active + _skill.ActiveOffsets[_skill.ActiveOffsets.Count-1] + 1;
            _current = new NpcActionSnapshot { InstanceId=++_nextInstance, Revision=1,
                Kind=NpcActionKind.Skill, Phase=NpcActionPhase.Windup, StartTick=tick, PhaseStartTick=tick,
                EffectiveTick=tick, SkillId=skillId, TargetId=targetId, PresentationId=_skill.PresentationId,
                ActiveStartTick=active, ActiveEndTick=recovery, RecoveryStartTick=recovery,
                EndTick=recovery+_skill.RecoveryTicks, AimX=target.Position.X,AimY=target.Position.Y,AimZ=target.Position.Z };
            _owner.AttackResolveTick = active;
            _owner.Moving = false;
            _store.GetPlayer(_owner.NpcId)!.ActionState = (byte)NpcAnimationState.Attack;
            return true;
        }
        public bool TryCommitStage(ulong instanceId, uint tick)
        {
            if (_current.InstanceId != instanceId || _current.Kind != NpcActionKind.Skill ||
                _current.Phase == NpcActionPhase.Cancelled || _current.Phase == NpcActionPhase.Completed) return false;
            if (!_store.GetPlayer(_owner.NpcId)!.IsAlive) { Die(tick); return false; }
            if (!ValidTarget(_current.TargetId)) { Cancel(NpcActionCancelReason.TargetInvalid,tick); return false; }
            if (_skill == null || _committedStages >= _skill.ActiveOffsets.Count ||
                !Due(tick,_current.ActiveStartTick+_skill.ActiveOffsets[_committedStages])) return false;
            if (_committedStages++ == 0) Phase(NpcActionPhase.Active,tick);
            return true;
        }
        public uint[] StageTicks()
        {
            var ticks = new uint[_skill!.ActiveOffsets.Count];
            for(int i=0;i<ticks.Length;i++) ticks[i]=_current.ActiveStartTick+_skill.ActiveOffsets[i];
            return ticks;
        }
        public void Advance(uint tick)
        {
            if (_current.Kind == NpcActionKind.Death) return;
            if (_current.Phase == NpcActionPhase.Cancelled || _current.Phase == NpcActionPhase.Completed)
            { if (tick != _current.EffectiveTick) SetContinuous(false,tick); return; }
            if (_current.Kind == NpcActionKind.HitReaction)
            { if (Due(tick,_current.EndTick)) SetContinuous(false,tick); return; }
            if (_current.Kind != NpcActionKind.Skill) return;
            if (!ValidTarget(_current.TargetId)) { Cancel(NpcActionCancelReason.TargetInvalid,tick); return; }
            if (_committedStages == _skill!.ActiveOffsets.Count && Due(tick,_current.RecoveryStartTick))
            {
                if (_current.Phase != NpcActionPhase.Recovery) Phase(NpcActionPhase.Recovery,tick);
                if (Due(tick,_current.EndTick)) { Phase(NpcActionPhase.Completed,tick); _owner.AttackResolveTick=null; }
            }
        }
        public void OnDirectDamage(uint tick)
        {
            bool skill = _current.Kind == NpcActionKind.Skill;
            if (_current.Kind == NpcActionKind.Death || (skill && (_committedStages > 0 || _armor))) return;
            if (skill) Cancel(NpcActionCancelReason.Hit,tick);
            if (_lastReaction.HasValue && unchecked(tick-_lastReaction.Value)<8) return;
            _lastReaction=tick;
            _current=new NpcActionSnapshot { InstanceId=++_nextInstance,Revision=1,Kind=NpcActionKind.HitReaction,
                Phase=NpcActionPhase.Active,StartTick=tick,PhaseStartTick=tick,EffectiveTick=tick,EndTick=tick+5,
                CancelReason=skill?NpcActionCancelReason.Hit:NpcActionCancelReason.None };
            _owner.HitStartTick=tick; _owner.Moving=false;
            _store.GetPlayer(_owner.NpcId)!.ActionState=(byte)NpcAnimationState.Hit;
        }
        public void Cancel(NpcActionCancelReason reason,uint tick)
        { if (_current.Kind==NpcActionKind.Death) return; _current.CancelReason=reason; Phase(NpcActionPhase.Cancelled,tick); _owner.AttackResolveTick=null; }
        public void ForceInterrupt(uint tick) => Cancel(NpcActionCancelReason.Forced,tick);
        public void Die(uint tick)
        {
            if (_current.Kind==NpcActionKind.Death) return;
            _current=new NpcActionSnapshot { InstanceId=++_nextInstance,Revision=1,Kind=NpcActionKind.Death,
                Phase=NpcActionPhase.Active,StartTick=tick,PhaseStartTick=tick,EffectiveTick=tick,EndTick=tick+MonsterModule.DeathDisplayTicks,CancelReason=NpcActionCancelReason.Death };
            _owner.AttackResolveTick=null; _owner.DeathTick=tick; _owner.Mode=MonsterMode.Dead;
            _owner.Threats.Clear(); _owner.TargetId=null; _owner.LastKnownTargetPosition=null; _owner.Moving=false;
            var entity=_store.GetPlayer(_owner.NpcId); if(entity!=null) entity.ActionState=(byte)NpcAnimationState.Death;
        }
        public void SetContinuous(bool moving,uint tick)
        {
            var kind=moving?NpcActionKind.Move:NpcActionKind.Idle;
            if (_current.InstanceId!=0 && _current.Kind==kind && _current.Phase==NpcActionPhase.Continuous) return;
            _current=new NpcActionSnapshot {InstanceId=++_nextInstance,Revision=1,Kind=kind,Phase=NpcActionPhase.Continuous,StartTick=tick,PhaseStartTick=tick,EffectiveTick=tick};
        }
        public void Capture(uint tick)
        {
            Advance(tick);
            if (_history.Count>0 && _history[_history.Count-1].Tick==tick) _history.RemoveAt(_history.Count-1);
            _history.Add((tick,Current));
            while(_history.Count>33) _history.RemoveAt(0);
        }
        public List<NpcActionSnapshot> History(uint afterTick)
        {
            if (_history.Count==0 || !Due(afterTick,_history[0].Tick)) throw new InvalidOperationException("npc-history-expired");
            var result=new List<NpcActionSnapshot>(); NpcActionSnapshot? baseline=null;
            foreach(var row in _history) { if(Due(afterTick,row.Tick)) baseline=row.State; else { if(baseline!=null) {result.Add(Copy(baseline));baseline=null;} result.Add(Copy(row.State));} }
            if(baseline!=null) result.Add(Copy(baseline));
            return result;
        }
        private void Phase(NpcActionPhase phase,uint tick) { _current.Phase=phase;_current.PhaseStartTick=tick;_current.EffectiveTick=tick;_current.Revision++; }
        private bool ValidTarget(long id) {var t=_store.GetPlayer(id);return t!=null && t.IsAlive && t.IsLoggedIn && Distance(_owner.Position,t.Position)<=_owner.Config.DropAggroRadiusOf;}
        internal static bool Due(uint now,uint due)=>unchecked((int)(now-due))>=0;
        private static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static NpcActionSnapshot Copy(NpcActionSnapshot s)=>new NpcActionSnapshot {InstanceId=s.InstanceId,Revision=s.Revision,Kind=s.Kind,Phase=s.Phase,StartTick=s.StartTick,PhaseStartTick=s.PhaseStartTick,EndTick=s.EndTick,SkillId=s.SkillId,TargetId=s.TargetId,PresentationId=s.PresentationId,CancelReason=s.CancelReason,ActiveStartTick=s.ActiveStartTick,ActiveEndTick=s.ActiveEndTick,RecoveryStartTick=s.RecoveryStartTick,AimX=s.AimX,AimY=s.AimY,AimZ=s.AimZ,AimYaw=s.AimYaw,PresentationVariant=s.PresentationVariant,EffectiveTick=s.EffectiveTick};
    }
}
