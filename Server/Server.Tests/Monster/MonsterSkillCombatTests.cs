using System.Collections.Generic;
using Cool.Protocol;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests.Monster;

public class MonsterSkillCombatTests
{
    private static (EntityStore Store, MonsterModule Module, MonsterState State) Make(int templateId)
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        // 出生点不要用原点：YawTowardOrigin(0,0,0) 是 atan2(-0,-0) 退化角，
        // 怪会面向 -Z；放在 (0,0,5) 让朝向稳定为 -Z，玩家放 z<5 即在视野锥内。
        long id = module.SpawnMonster(templateId, new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 5f));
        return (store, module, module.GetState(id)!);
    }

    private static PlayerState Login(EntityStore store, long id, Vector3 position)
    {
        var player = store.AddPlayer(id, $"P{id}");
        player.IsLoggedIn = true;
        player.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return player;
    }

    [Fact]
    public void DogKnight_Below_Half_Hp_Uses_MidRange_Skill_At_4m()
    {
        var (store, module, state) = Make(3);
        var entity = store.GetPlayer(state.NpcId)!;
        entity.ApplyDamage(200f); // 300 -> 100（< 50%）
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m，在 -Z 视野锥内

        var casts = new List<CastRequest>();
        for (uint t = 0; t < 30; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts()) casts.Add(request);
        }

        Assert.Contains(casts, r => r.SkillId == 50005);
        Assert.DoesNotContain(casts, r => r.SkillId == 50003); // 4m 超出 50003
    }

    [Fact]
    public void DogKnight_Above_Half_Hp_Chases_Into_Melee_Range()
    {
        var (store, module, state) = Make(3);
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m

        for (uint t = 0; t < 120; t++) module.Tick(t);
        var casts = module.DrainPendingCasts();

        Assert.Contains(casts, c => c.Request.SkillId == 50003);
        Assert.DoesNotContain(casts, c => c.Request.SkillId == 50005);
    }

    [Fact]
    public void SpiderBoss_Below_Half_Hp_Uses_Aoe()
    {
        var (store, module, state) = Make(4);
        var entity = store.GetPlayer(state.NpcId)!;
        entity.ApplyDamage(1000f); // 1500 -> 500（< 50%）
        Login(store, 1, new Vector3(0f, 0f, 2f)); // 距出生点 3m

        var casts = new List<CastRequest>();
        for (uint t = 0; t < 30; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts()) casts.Add(request);
        }

        Assert.Contains(casts, r => r.SkillId == 50006);
        Assert.Equal(1, casts.Find(r => r.SkillId == 50006)!.Shape.ShapeType);
    }

    [Fact]
    public void Attack_Does_Not_Switch_Skill_During_Windup()
    {
        var (store, module, state) = Make(3);
        var entity = store.GetPlayer(state.NpcId)!;
        entity.ApplyDamage(200f);
        Login(store, 1, new Vector3(0f, 0f, 1f)); // 距出生点 4m

        uint decideTick = 0;
        CastRequest? first = null;
        for (uint t = 0; t < 30 && first == null; t++)
        {
            module.Tick(t);
            foreach (var (_, request) in module.DrainPendingCasts())
            {
                decideTick = t;
                first = request;
            }
        }
        Assert.NotNull(first);
        Assert.True(state.AttackResolveTick.HasValue);

        // 前摇期间给另一目标 1000 威胁：TargetId 不变
        Login(store, 2, new Vector3(0f, 0f, 0.5f)); // 距出生点 4.5m
        state.Threats.Add(2, 1000f);
        module.Tick(decideTick + 1);
        Assert.Equal(1L, state.TargetId);
    }
}
