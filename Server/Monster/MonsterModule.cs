using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Config;

namespace KcpServer.Monster
{
    /// <summary>
    /// P5 怪物模块（spec §5-P5 完整版）——主循环线程独占（R2），无锁。
    /// 每 tick：受击/死亡转移检测 → 感知（视野锥内最近存活已登录玩家）→ 行为树
    /// （巡逻/追击/攻击/返回）→ 位置 flush（UpdatePosition）→ 待施法队列
    /// （Server3C drain 后经 CombatResolver 裁决，T3）。
    /// 死亡：Death 态展示 DeathDisplayTicks → 移除实体（Server3C 清 AOI）→
    /// RespawnTicks 后新 id 满血重生在巡逻中心。
    /// 断线持续运转：状态全在 EntityStore + 本模块，零 per-session 依赖（spec 验收）。
    /// </summary>
    public sealed class MonsterModule
    {
        public const long NpcIdSeed = 2_000_000;  // NPC id 段（BotRunner 占 1_000_000+）
        public const uint DeathDisplayTicks = 30; // 死亡展示 30 tick ≈ 2s（客户端播完死亡态过期销毁）
        public const uint RespawnTicks = 450;     // 重生 30s（15Hz × 30；与客户端 MonsterSpawner.RespawnDelay 同值）
        public const uint HitDisplayTicks = 5;    // 受击硬直 5 tick
        public const float TickDeltaSeconds = 0.066f; // 主循环 66ms

        private readonly EntityStore _store;
        private readonly Func<long> _nextNpcId;
        private readonly Dictionary<int, MonsterData> _templates = new();
        private readonly Dictionary<long, MonsterState> _monsters = new();
        private readonly SimpleMoveSystem _mover = new();
        private readonly List<(long NpcId, CastRequest Request)> _pendingCasts = new();
        private readonly List<long> _removedNpcs = new();
        private readonly List<(int TemplateId, Vector3 Center, uint DueTick)> _respawns = new();

        /// <summary>活跃怪物数（测试/统计口径）</summary>
        public int Count => _monsters.Count;

        /// <summary>模板 → 战斗技能 id。**public 自 2026-10-12**：测试要断言
        /// 「配置 attackRange ≤ 表上限」（防静默冻结），需要拿到映射。</summary>
        public static int SkillIdOf(int templateId) => templateId switch
        {
            1 => 50001,
            2 => 50002,
            3 => 50003,   // DogKnight 精英
            4 => 50004,   // SpiderBoss
            _ => throw new ArgumentException($"No combat skill mapped for monster template {templateId}")
        };

        public MonsterModule(EntityStore store, Func<long> nextNpcId, string? configPath = null)
        {
            _store = store;
            _nextNpcId = nextNpcId;
            // 配置路径按程序集基准目录解析（CWD 相对路径在 dotnet run/test 不同 CWD 下静默落默认值——
            // csproj CopyToOutputDirectory 保证 json 在输出目录；找不到仍落 ConfigLoader 默认值）
            var path = configPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Config", "monster_config.json");
            var templates = ConfigLoader.LoadMonsterTemplates(path);
            foreach (var kvp in templates) _templates[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// 固定布局生成（e2e 确定性依赖——T5 注释同步此表）：
        /// 前四点位是 e2e 既有夹具（观察者在原点）：
        ///   [0] Wolf(t2)  (12,0,0)  面向原点——开局即感知原点观察者（检测 15m ≥ 12m）
        ///   [1] Slime(t1) (40,0,0)  巡逻可视（观察者视野 80m）、不感知（检测 8m）
        ///   [2] Slime(t1) (-40,0,0) 同 [1]
        ///   [3] Wolf(t2)  (0,0,40)  面向原点（观察者视野 80m 内，不感知——距离 40m > 15m）
        /// 全部六槽位按 **DemoDay 玩家出生点 (10,0,-40)**（DemoDay.unity 的 PlayerSpawner._spawnPoint 实测，
        /// 2026-10-13 从 (10,0,-15.52) 搬到湖东岸台地）避让——原点只是 e2e 观察者的位置，按原点推算会漏判：
        /// 槽位距出生点须 > 感知半径 + 巡逻半径，否则怪物走完巡逻圈就能感知到原地不动的玩家。
        ///（实测教训：DogKnight 初版 (0,0,-20) 距旧出生点仅 11.0m < 感知 18m → 12 分钟内 8/8 次进世界
        ///  打死挂机玩家；旧出生点下 [0] Wolf 是唯一按设计违反本规则的槽位（巡逻圈最近切到 5.9m），
        ///  新出生点下它也达标——MonsterModuleTests.New_Layout_Slots_Stay_Out_Of_Reach_Of_Demo_Spawn
        ///  现覆盖全部六槽位，行为侧由 Idle_Player_At_Demo_Spawn_Sees_No_Cast_From_Any_Monster 守。）
        ///   [0] Wolf(t2) (12,0,0) 距出生点 40.1m（> 15+10）——对原点观察者开局即感知（12m ≤ 15m，e2e 夹具）
        ///   [4] DogKnight(t3) (-20,0,20)    距出生点 67.1m（> 18+6）——对原点观察者 28.3m（> 18）亦不仇恨
        ///   [5] SpiderBoss(t4) (-40,0,-25)  距出生点 52.2m（> 25+12）——对原点观察者 47.2m（> 25）亦不仇恨
        /// count 超过布局长度时循环复用；朝向一律面向原点（感知确定性）。
        /// </summary>
        public void SpawnDefaultLayout(int count)
        {
            var layout = new (int TemplateId, Vector3 Position)[]
            {
                (2, new Vector3(12f, 0f, 0f)),
                (1, new Vector3(40f, 0f, 0f)),
                (1, new Vector3(-40f, 0f, 0f)),
                (2, new Vector3(0f, 0f, 40f)),
                (3, new Vector3(-20f, 0f, 20f)),
                (4, new Vector3(-40f, 0f, -25f)),
            };
            for (int i = 0; i < count; i++)
            {
                var slot = layout[i % layout.Length];
                SpawnMonster(slot.TemplateId, slot.Position, patrolCenter: slot.Position);
            }
        }

        /// <summary>生成怪物（AddNpc 满血 + 行为树装配 + 巡逻点）</summary>
        public long SpawnMonster(int templateId, Vector3 position, Vector3 patrolCenter)
        {
            if (!_templates.TryGetValue(templateId, out var config))
                throw new ArgumentException($"Unknown monster template: {templateId}");

            // 攻击技能 fail fast（终审 follow-up）：SkillIdOf 未映射模板已抛，这里再校验技能在
            // CombatRules 表内——若拖到 BT Attack 才 TryGet 失败，怪物会在攻击距离静默冻结
            //（零日志零计数，"npc cast rejected = 0" 的验收口径看不出这类死重）
            int attackSkillId = SkillIdOf(templateId);
            if (!CombatRules.TryGet(attackSkillId, out _))
                throw new ArgumentException($"Monster skill {attackSkillId} (template {templateId}) missing from CombatRules table");

            long npcId = _nextNpcId();
            var entity = _store.AddNpc(npcId, $"{config.name}#{npcId}", templateId, config.hp, position);
            entity.Rotation = YawTowardOrigin(position);

            var state = new MonsterState(npcId, templateId, config, attackSkillId)
            {
                Position = position,
                Rotation = Quaternion.LookRotation(ForwardOf(entity.Rotation)),
                SpawnPosition = position,
                PatrolCenter = patrolCenter,
                PatrolPoints = GeneratePatrolPoints(patrolCenter, config.patrolRadius),
                LastHp = config.hp
            };
            state.BehaviorTree = BuildBehaviorTree();
            _monsters[npcId] = state;
            return npcId;
        }

        /// <summary>测试/调试：读怪物 AI 状态</summary>
        public MonsterState? GetState(long npcId) => _monsters.TryGetValue(npcId, out var s) ? s : null;

        /// <summary>推进一 tick（服务器主循环每 tick 调用——先于施法裁决 drain 与快照广播）</summary>
        public void Tick(uint tick)
        {
            var done = new List<long>();
            foreach (var m in _monsters.Values)
            {
                var entity = _store.GetPlayer(m.NpcId);
                if (entity == null) { done.Add(m.NpcId); continue; }

                if (m.DeathTick.HasValue)
                {
                    // 死亡展示窗口：原地停驻 Death 态，到期移除 + 调度重生
                    if (tick - m.DeathTick.Value >= DeathDisplayTicks)
                    {
                        _store.RemovePlayer(m.NpcId);
                        _removedNpcs.Add(m.NpcId); // Server3C 清 AOI 状态
                        _respawns.Add((m.TemplateId, m.PatrolCenter, tick + RespawnTicks));
                        done.Add(m.NpcId);
                    }
                    continue;
                }

                DetectHitOrDeath(m, entity, tick);

                bool deadNow = m.DeathTick.HasValue;
                bool inHitWindow = m.HitStartTick.HasValue && tick - m.HitStartTick.Value < HitDisplayTicks;
                if (!deadNow && !inHitWindow)
                {
                    DetectTarget(m, entity);
                    var ctx = new MonsterTickContext(m, entity, this, _mover, TickDeltaSeconds, tick);
                    m.BehaviorTree?.Tick(ctx);
                }
                else
                {
                    m.Moving = false; // 本 tick 死亡/受击硬直：跳过行为树（ActionState 保持 Death/Hit 不被 BT 覆盖）
                }

                // 硬直窗口**刚过期那一 tick**的状态写回（终审 I1）：本 tick BT 已恢复，但 Chase 在攻击距离内
                // `return Success` 不写状态、Attack 在冷却门未过时 `return Running` 也不写——近战互殴（目标
                // 一直留在攻击距离内）时 ActionState 会从受击那刻停在 Hit，直到下一次真正施法或目标离开。
                // 客户端持续采到 Hit → 动画停在受击态（DogKnight 的 GetHit 循环播；蜘蛛 hit1 定格末帧）。
                // 只在"上一 tick 还在窗口内、这一 tick 刚出窗口"这一次补（每 tick 写会提前释放客户端忙位、
                // 当场打断挥砍）；且只在 BT 没写（仍是 Hit）时补——否则会盖掉同 tick 的 Attack 置位。
                bool hitWindowJustExpired = m.HitStartTick.HasValue
                    && tick - m.HitStartTick.Value == HitDisplayTicks;
                if (hitWindowJustExpired && entity.ActionState == (byte)NpcAnimationState.Hit)
                    entity.ActionState = (byte)(m.Moving ? NpcAnimationState.Run : NpcAnimationState.Idle);

                // 位置/朝向/速度 flush（AOI 节拍由调度器按坐标判定——每 tick 写与 BotRunner 同惯例）
                entity.UpdatePosition(m.Position, YawOf(m.Rotation), m.Moving ? m.Config.moveSpeed : 0f,
                    tick * 10_000L + 1L, tick);
            }
            foreach (var id in done) _monsters.Remove(id);

            // 重生到期（同 tick 生成——快照广播在 drain 之后，首帧立即可见）
            for (int i = _respawns.Count - 1; i >= 0; i--)
            {
                var (templateId, center, dueTick) = _respawns[i];
                if (tick - dueTick < uint.MaxValue / 2) // wrap 安全到期判定（tick 差相减惯例）
                {
                    _respawns.RemoveAt(i);
                    SpawnMonster(templateId, center, center);
                }
            }
        }

        /// <summary>受击/死亡转移检测（裁决在怪物 tick 之后发生——本 tick 察觉上一 tick 的 HP 变化）</summary>
        private void DetectHitOrDeath(MonsterState m, PlayerState entity, uint tick)
        {
            if (entity.Hp < m.LastHp)
            {
                m.LastHp = entity.Hp;
                if (!entity.IsAlive)
                {
                    m.DeathTick = tick;
                    m.Moving = false;
                    entity.ActionState = (byte)NpcAnimationState.Death;
                }
                else
                {
                    m.HitStartTick = tick;
                    entity.ActionState = (byte)NpcAnimationState.Hit;
                }
            }
        }

        /// <summary>感知：视野锥内最近的存活已登录玩家（D-P5-9——目标丢失才切换；AggroTable 仇恨排名留 backlog）</summary>
        private void DetectTarget(MonsterState m, PlayerState entity)
        {
            long? closestId = null;
            Vector3? closestPos = null;
            float closestDistSqr = float.MaxValue;

            foreach (var p in _store.All)
            {
                if (p.Kind != (byte)EntityKind.Player || !p.IsAlive || !p.IsLoggedIn) continue;
                if (!TargetDetector.CanDetect(entity.Rotation, m.Position, p.Position,
                        m.Config.detectionRadius, m.Config.visionAngle)) continue;
                float dx = p.Position.X - m.Position.X, dz = p.Position.Z - m.Position.Z;
                float distSqr = dx * dx + dz * dz;
                if (distSqr < closestDistSqr) { closestDistSqr = distSqr; closestId = p.PlayerId; closestPos = p.Position; }
            }

            if (closestId.HasValue)
            {
                if (m.TargetId != closestId)
                {
                    m.TargetId = closestId;
                    m.AlertLevel = AlertLevel.HOSTILE; // 获取目标即敌意（旧语义）
                }
                m.LastKnownTargetPosition = closestPos;
            }
            else if (m.TargetId.HasValue)
            {
                m.TargetId = null; // 目标丢失（离视野/死亡/离线）——树落 Return 分支
            }
        }

        /// <summary>入队攻击施法（BT Attack 动作调用；Server3C 每 tick drain 后裁决+广播——T3）</summary>
        public void QueueCast(long npcId, CastRequest request) => _pendingCasts.Add((npcId, request));

        /// <summary>取出本 tick 待裁决施法（Server3C drain）</summary>
        public List<(long NpcId, CastRequest Request)> DrainPendingCasts()
        {
            var result = new List<(long, CastRequest)>(_pendingCasts);
            _pendingCasts.Clear();
            return result;
        }

        /// <summary>取出本 tick 已移除怪物（Server3C 清 AOI 状态）</summary>
        public List<long> DrainRemovedNpcs()
        {
            var result = new List<long>(_removedNpcs);
            _removedNpcs.Clear();
            return result;
        }

        private BtNode BuildBehaviorTree()
        {
            return new BtSelector(
                new BtSequence(
                    new BtCondition(m => m.AlertLevel == AlertLevel.PEACE && !m.TargetId.HasValue),
                    new BtAction(MonsterBtActions.Patrol)
                ),
                new BtSequence(
                    new BtCondition(m => m.AlertLevel == AlertLevel.HOSTILE && m.TargetId.HasValue),
                    new BtAction(MonsterBtActions.Chase),
                    new BtAction(MonsterBtActions.Attack)
                ),
                new BtSequence(
                    new BtCondition(m => !m.TargetId.HasValue && m.AlertLevel != AlertLevel.PEACE),
                    new BtAction(MonsterBtActions.Return)
                )
            );
        }

        private static Vector3[] GeneratePatrolPoints(Vector3 center, float radius)
        {
            const int count = 4;
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float rad = i * (360f / count) * (float)(Math.PI / 180.0);
                points[i] = new Vector3(
                    center.X + (float)Math.Cos(rad) * radius,
                    center.Y,
                    center.Z + (float)Math.Sin(rad) * radius);
            }
            return points;
        }

        /// <summary>yaw（度）→ 前向量（yaw 语义与 HitResolver 一致：yaw=0 → +Z）</summary>
        private static Vector3 ForwardOf(float yawDeg)
        {
            float rad = yawDeg * (float)(Math.PI / 180.0);
            return new Vector3((float)Math.Sin(rad), 0f, (float)Math.Cos(rad));
        }

        /// <summary>Quaternion → yaw 度（旧 NpcSyncBroadcaster.GetYaw 同公式）</summary>
        private static float YawOf(Quaternion q)
            => (float)(Math.Atan2(2 * (q.W * q.Y + q.X * q.Z), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 180.0 / Math.PI);

        /// <summary>面向原点的 yaw（固定布局 e2e 感知确定性）</summary>
        private static float YawTowardOrigin(Vector3 position)
        {
            float rad = (float)Math.Atan2(-position.X, -position.Z);
            return rad * (float)(180.0 / Math.PI);
        }
    }
}
