using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Monster;
using Xunit;
namespace KcpServer.Tests;
public class MonsterActionRunnerTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void Direct_damage_during_windup_cancels_ordinary_but_not_armor(int template, bool armored)
    {
        var store = new EntityStore(); long seed = 2000000;
        var module = new MonsterModule(store, () => ++seed, actionV1: true);
        long id = module.SpawnMonster(template, new Vector3(0,0,0), new Vector3(0,0,0));
        var player = store.AddPlayer(1,"P"); player.IsLoggedIn = true;
        player.Position = new Vector3(0,0,1);
        store.GetPlayer(id)!.Rotation = 0;
        var state = module.GetState(id)!;
        state.TargetId = 1; state.LastKnownTargetPosition = player.Position;
        var selected = SkillSelector.Select(state,store.GetPlayer(id)!,1,0)!.Value;
        Assert.True(MonsterCastBuilder.TryBuild(state,selected,player.Position,out var request));
        var resolver = new CombatResolver(store,new Random(1)) { NpcActionRunner = n => module.GetState(n)?.ActionRunner };
        Assert.True(resolver.Decide(id,request,0).Accepted);
        store.GetPlayer(id)!.Hp -= 1;
        module.OnDamageTaken(id,1,1,MonsterDamageKind.Direct);
        var hits = resolver.DrainResolved(6);
        int count = 0; foreach(var cast in hits) count += cast.Hits.Count;
        Assert.Equal(armored ? 1 : 0,count);
        Assert.False(store.GetPlayer(id)!.IsCastReady(request.SkillId,1,CombatRules.CooldownTicks(selected.Entry)));
    }
    private static (EntityStore Store, MonsterModule Module, MonsterState State, CombatResolver Resolver, CastRequest Request) Fixture(int template=1, bool multi=false)
    {
        var store=new EntityStore();long seed=2000000;
        var catalog=NpcCatalogDefaults.Create();
        if(multi) catalog.Skills.Find(s=>s.SkillId==50001)!.ActiveOffsets.Add(2);
        var module=new MonsterModule(store,()=>++seed,actionV1:true,catalog:catalog);
        var id=module.SpawnMonster(template,new Vector3(0,0,0),new Vector3(0,0,0));
        var player=store.AddPlayer(1,"P");player.IsLoggedIn=true;player.Position=new Vector3(0,0,1);player.Rotation=180;
        var state=module.GetState(id)!;state.TargetId=1;state.LastKnownTargetPosition=player.Position;
        store.GetPlayer(id)!.Rotation=0;
        var selected=SkillSelector.Select(state,store.GetPlayer(id)!,1,0)!.Value;
        Assert.True(MonsterCastBuilder.TryBuild(state,selected,player.Position,out var request));
        var resolver=new CombatResolver(store,new Random(1)) {NpcActionRunner=n=>module.GetState(n)?.ActionRunner};
        return(store,module,state,resolver,request);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Ordered_first_active_stage_preserves_already_committed_hits(bool playerFirst)
    {
        var f=Fixture(multi:true);
        f.Resolver.NpcDamaged=(id,attacker,damage)=>f.State.ActionRunner!.OnDirectDamage(6);
        var attack=new CastRequest {SkillId=f.Request.SkillId,TargetPlayerId=f.State.NpcId,Shape=f.Request.Shape};
        if(playerFirst) Assert.True(f.Resolver.Decide(1,attack,0).Accepted);
        Assert.True(f.Resolver.Decide(f.State.NpcId,f.Request,0).Accepted);
        if(!playerFirst) Assert.True(f.Resolver.Decide(1,attack,0).Accepted);
        int npcHits=0;
        foreach(var cast in f.Resolver.DrainResolved(6)) if(cast.IsNpcCaster) npcHits+=cast.Hits.Count;
        foreach(var cast in f.Resolver.DrainResolved(8)) if(cast.IsNpcCaster) npcHits+=cast.Hits.Count;
        Assert.Equal(playerFirst?0:2,npcHits);
    }
    [Fact]
    public void Empty_geometry_commits_stage_and_finishes_recovery_without_new_cast()
    {
        var f=Fixture(); Assert.True(f.Resolver.Decide(f.State.NpcId,f.Request,0).Accepted);
        f.Store.GetPlayer(1)!.Position=new Vector3(0,0,8);
        var result=Assert.Single(f.Resolver.DrainResolved(6));Assert.Empty(result.Hits);
        Assert.Equal(NpcActionPhase.Active,f.State.ActionRunner!.Current.Phase);
        f.State.ActionRunner.Advance(7);Assert.Equal(NpcActionPhase.Recovery,f.State.ActionRunner.Current.Phase);
        f.State.ActionRunner.OnDirectDamage(8);Assert.Equal(NpcActionPhase.Recovery,f.State.ActionRunner.Current.Phase);
        Assert.False(f.State.ActionRunner.CanStartSkill);
        f.State.ActionRunner.Advance(12);Assert.Equal(NpcActionPhase.Completed,f.State.ActionRunner.Current.Phase);
        f.State.ActionRunner.Advance(13);Assert.True(f.State.ActionRunner.CanStartSkill);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Dead_or_missing_locked_target_cancels_even_armored(bool death)
    {
        var f=Fixture(4); Assert.True(f.Resolver.Decide(f.State.NpcId,f.Request,0).Accepted);
        if(death) f.Store.GetPlayer(1)!.Hp=0;else f.Store.RemovePlayer(1);
        Assert.Empty(f.Resolver.DrainResolved(6));
        Assert.Equal(NpcActionCancelReason.TargetInvalid,f.State.ActionRunner!.Current.CancelReason);
    }
    [Fact]
    public void Snapshot_is_frozen_and_terminal_history_is_bounded()
    {
        var f=Fixture();Assert.True(f.Resolver.Decide(f.State.NpcId,f.Request,0).Accepted);
        var frozen=f.State.ActionRunner!.Current;
        f.State.ActionRunner.Cancel(NpcActionCancelReason.Forced,1);
        Assert.Equal(NpcActionPhase.Windup,frozen.Phase);
        for(uint t=1;t<40;t++) f.Module.CaptureActionHistory(t);
        Assert.Throws<InvalidOperationException>(()=>f.Module.GetActionHistory(f.State.NpcId,1));
        Assert.InRange(f.Module.GetActionHistory(f.State.NpcId,7).Count,1,33);
    }
    [Fact]
    public void Low_hp_DogKnight_sets_up_special_then_casts_in_legal_range()
    {
        var f=Fixture(3);var self=f.Store.GetPlayer(f.State.NpcId)!;self.Hp=self.MaxHp*0.4f;f.State.LastHp=self.Hp;
        f.State.Mode=MonsterMode.Combat;f.State.Threats.Ensure(1);
        bool moved=false,cast=false;
        for(uint t=0;t<30;t++) {
            f.Module.Tick(t);moved |= f.State.DesiredSkillId==50005 && f.State.Moving;
            foreach(var pending in f.Module.DrainPendingCasts()) {
                if(pending.Request.SkillId!=50005) continue;
                float d=MathF.Abs(f.Store.GetPlayer(1)!.Position.Z-self.Position.Z);
                Assert.InRange(d,2,6);Assert.True(f.Resolver.Decide(pending.NpcId,pending.Request,t).Accepted);cast=true;
            }
            if(cast) break;
        }
        Assert.True(moved);Assert.True(cast);
    }

}
