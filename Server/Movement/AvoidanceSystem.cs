using System;
using System.Collections.Generic;
using Cool.Protocol;

namespace KcpServer.Movement
{
    /// <summary>
    /// ORCA 避让的**世界接线层**（2026-10-13，第二步）——把"世界里现在有谁、在哪、多快、多大"
    /// 喂给纯算法 <see cref="OrcaSolver"/>，再把解出的速度交回各移动系统
    /// （<c>SimpleMoveSystem.MoveTo</c> / <c>BotRunner.Tick</c>）。解算器本身不认识世界，本类不碰积分。
    ///
    /// 三条约定（都是接线任务规格，改之前先想清楚）：
    ///
    /// 1. **Jacobi 快照**：每 tick 开头拍一份"位置 + 速度"快照（<see cref="EnsureSnapshot"/>），
    ///    之后所有智能体都对着这一份解算 ⇒ 结果与"先解谁后解谁"无关（快照按 id 排序，
    ///    连字典遍历顺序都不影响约束顺序）。位置/速度的**应用**仍在各移动系统里就地做——
    ///    因为自身上下一次解算读的是快照，就地应用不会改变同 tick 其它智能体的输入。
    ///
    /// 2. **速度单位 = 每 tick 位移（米/tick）**，不是 m/s。这样"期望速度 v_pref"就是各移动系统
    ///    原本要走的位移，解算器无邻居时**逐字**返回它 ⇒ 接线后既有行为逐位不变（护栏，见测试
    ///    <c>No_Neighbors_...</c>）。τ 也是 tick 单位。实体上报的 m/s 经
    ///    <see cref="_secondsPerTick"/> 换算进来（只影响"邻居被当成多快"，不影响自己的积分）。
    ///
    /// 3. **责任权重 α**：怪物/机器人互相 0.5（两边都在服务端跑同一套解算，各让一半）；
    ///    真人玩家 1.0（服务端改不了客户端权威的速度 ⇒ 智能体全责绕开），且玩家**按零速度障碍**处理
    ///    （不用上报速度推——理由见 <see cref="EnsureSnapshot"/> 里的注释：朝向≠移动方向，且不可信输入
    ///    不该决定安全保证）。
    ///
    /// 已知取舍：邻居筛是**一次线性扫**（n² / 2 次距离比较，6 怪 + ~100 bot + 玩家 ≈ 一万次 @15Hz，
    /// 可忽略），不引空间哈希（明确不做）。筛程 = r_i + r_j + <see cref="NeighborMargin"/>。
    /// </summary>
    public sealed class AvoidanceSystem
    {
        /// <summary>真人玩家的身体半径（米）：服务端不持有玩家胶囊，取客户端两份玩家预制体的
        /// <c>CharacterController.m_Radius</c> 实测值（FemalePlayer.prefab / MaleCharacterPBR.prefab 都是 0.5）。</summary>
        public const float PlayerRadius = 0.5f;

        /// <summary>模板表里查不到时的兜底半径（= 客户端球体占位的量级；见 MonsterData.bodyRadius）</summary>
        public const float DefaultMonsterRadius = 0.5f;

        /// <summary>
        /// 邻居距离筛的**近场余量**（米）：<c>reach = r_i + r_j + NeighborMargin + (|v_i| + |v_j|)·τ</c>。
        ///
        /// 后一项是**时间视界对应的"可能撞上"距离**（速度按每 tick 位移算，τ 按 tick 算，量纲自洽）：
        /// 筛程必须覆盖 τ 内能走完的距离，τ 才不是一句空话。**固定 1.5m 的筛程实测不够用**——
        /// 8m/s 的两个机器人正面对撞时，1.5m 余量意味着接触前 1~3 tick 才看见对方，
        /// 解出来的速度来不及让开，实测最小间距 0.35m（半径和 1.0，即穿模 0.65m）。
        /// 加上速度项后同类场景在 τ 满程（10 tick）前就进入视野。
        ///
        /// 代价可忽略：线性扫的 n²/2 次比较**本来就要做**（筛程只影响比较后的取舍），
        /// 空旷处（100 bot / ±400m）10m 筛程内的期望邻居数 ≈ 0.05；把远处的非冲突邻居喂进解算器
        /// 也不会平白绕路——ORCA 对"τ 内不会撞"的邻居不产生约束（OrcaSolverTests 有钉子）。
        /// </summary>
        public const float NeighborMargin = 1.5f;

        /// <summary>
        /// 邻居缓冲上限。取 128 ≥ 全场理论上限（--bots 100 + 6 怪 + 玩家，且自己不在其中）⇒ 任何配置下
        /// 都不会溢出。真溢出时**只跳过写不下的那些、继续扫完**（不 break——break 会把还没扫到的
        /// 邻居**全部**丢掉，100 bot 挤到一处时那正是最需要保护的几对；审查把这条点名为"人群挤压的悬崖"）。
        /// </summary>
        private const int MaxNeighbors = 128;

        /// <summary>退化 tie-break 的偏移量（米）：只用来把数值上完全对称的几何掰开，
        /// 比任何真实半径小 3 个数量级（0.1mm），也远大于浮点噪声（~1e-7 相对值）。</summary>
        private const float TieBreakOffset = 1e-4f;

        private readonly EntityStore _store;
        private readonly Func<int, float> _npcRadiusOf;
        private readonly float _secondsPerTick;
        private readonly OrcaSolver _solver;
        private readonly List<SnapshotAgent> _agents = new List<SnapshotAgent>();
        private readonly OrcaNeighbor[] _neighborBuffer = new OrcaNeighbor[MaxNeighbors];

        private uint _snapshotTick;
        private bool _hasSnapshot;

        /// <summary>避让总开关（CLI <c>--no-avoid</c> 的负对照）。关掉后 <see cref="Solve"/> 原样返回期望位移。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>当前快照里的智能体数（诊断/测试用）</summary>
        public int AgentCount => _agents.Count;

        /// <summary>时间视界 τ（tick；测试按同一 τ 校验"解出的速度在 τ 内不撞"）</summary>
        public float TimeHorizonTicks => _solver.TimeHorizon;

        /// <summary>本 tick 上一次解算是否走了 LP3 回退（诊断/测试用，透传解算器的同名属性）</summary>
        public bool LastSolveUsedFallback => _solver.LastSolveUsedFallback;

        /// <summary>本 tick 上一次解算实际喂进解算器的邻居数（诊断用——筛程/快照是否正确看它）</summary>
        public int LastNeighborCount { get; private set; }

        /// <param name="store">世界实体表（唯一真相源：玩家/机器人/怪物都在里面）</param>
        /// <param name="npcRadiusOf">模板 id → 身体半径（怪物侧注入 <c>MonsterModule.RadiusOfTemplate</c>）</param>
        /// <param name="secondsPerTick">主循环 tick 秒数（0.066）——上报速度 m/s → 米/tick 的换算</param>
        /// <param name="timeHorizonTicks">τ（tick 单位）。解算器 Δt 固定 1 tick（重叠时"一个 tick 内推开"）</param>
        public AvoidanceSystem(EntityStore store, Func<int, float> npcRadiusOf,
            float secondsPerTick = 0.066f, float timeHorizonTicks = 20f)
        {
            _store = store;
            _npcRadiusOf = npcRadiusOf;
            _secondsPerTick = secondsPerTick;
            _solver = new OrcaSolver(timeHorizonTicks, 1f);
        }

        /// <summary>
        /// 每 tick 开头重建快照。**同一 tick 重复调用是 no-op**——Server3C 在主循环 tick 起始处调一次
        /// （机器人 tick / 怪物 tick 都在它之后，两边看到同一份世界），MonsterModule 独立运行时
        /// （单测、无 Server3C 的宿主）在自己的 Tick 顶部再叫一次，此时才真正重建。
        /// </summary>
        public void EnsureSnapshot(uint tick)
        {
            if (_hasSnapshot && _snapshotTick == tick) return;
            _snapshotTick = tick;
            _hasSnapshot = true;

            _agents.Clear();
            foreach (var e in _store.All)
            {
                if (!e.IsAlive) continue;                                        // 尸体不挡路
                byte kind = e.Kind;
                if (kind == (byte)EntityKind.Player && !e.IsLoggedIn) continue;   // 未登录真人（位置不可信）
                // 压测 bot **不算**登录态（D-P5-3），所以上面那条只筛真人 —— 这里按 kind 判参与者
                bool participates = kind != (byte)EntityKind.Player;

                // 速度：**真人玩家一律按零速度障碍**（规格要求），怪物/机器人取上一 tick flush 的
                // Speed × 朝向。为什么玩家不用上报速度（2026-10-13 审查裁定，曾在接线时用上报值推过）：
                //   1. `PlayerState.Rotation` 是**角色朝向**，不是移动方向——侧移/后退/转身时两者不同，
                //      按朝向推速度会把玩家当成"沿面朝方向飞出去"的障碍，约束算错方向；
                //   2. 上报速度是**客户端可控输入**：报一个大速度朝反方向，就能让智能体"认为 τ 内
                //      不会相遇"而根本不产约束（直接穿过去）。安全保证不能建立在不可信输入上。
                // 代价落在保守侧：玩家真在跑时智能体按"他没动"来让——躲得笨一点，不会穿模
                //（何况客户端那条 NpcBodyCollider 本来就挡着本地玩家）。
                float vx = 0f, vz = 0f, speedPerTick = 0f;
                if (participates)   // 怪物 / 压测机器人：服务端自己写的 Speed + 朝向（朝向 = 移动方向）
                {
                    float yawRad = e.Rotation * (float)(Math.PI / 180.0);
                    speedPerTick = e.Speed * _secondsPerTick;
                    vx = (float)Math.Sin(yawRad) * speedPerTick;
                    vz = (float)Math.Cos(yawRad) * speedPerTick;
                }
                _agents.Add(new SnapshotAgent
                {
                    Id = e.PlayerId,
                    X = e.Position.X,
                    Z = e.Position.Z,
                    VX = vx,
                    VZ = vz,
                    Speed = speedPerTick,   // 玩家恒 0 ⇒ 邻居侧筛程随之变小（保守 ✓）
                    Radius = BodyRadiusOf(e),
                    Participates = participates,
                });
            }

            // 按 id 排序：约束顺序只由**世界状态**决定，与字典遍历顺序/历史插入删除无关（确定性）
            _agents.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        }

        /// <summary>实体身体半径（米；诊断探针也走这里，保证两处口径一致）</summary>
        public float BodyRadiusOf(PlayerState e) => e.Kind switch
        {
            (byte)EntityKind.Npc => _npcRadiusOf(e.TemplateId),
            (byte)EntityKind.PlayerBot => BotRunner.BotRadius,   // 常量定义处见 BotRunner（占位胶囊量级）
            _ => PlayerRadius,
        };

        /// <summary>
        /// 解算一个智能体本 tick 的位移。**没有邻居时逐字返回 <paramref name="preferred"/>**
        /// （护栏：接线不改变既有行为；不依赖解算器内部的截断分支，见方法内注释）。
        /// </summary>
        /// <param name="selfId">自身实体 id（把自己从邻居里去掉）</param>
        /// <param name="excludeId">本 tick 要排除的邻居——怪物的当前攻击目标（否则追击的怪绕着目标转，
        /// 永远进不了 attackRange）</param>
        /// <param name="position">自身位置（快照同 tick 的位置）</param>
        /// <param name="velocity">自身当前速度（米/tick；上一 tick 的实际位移）</param>
        /// <param name="preferred">期望位移（米/tick）</param>
        /// <param name="radius">自身半径</param>
        /// <param name="maxSpeed">速度上限（米/tick）</param>
        public Vector3 Solve(long selfId, long? excludeId, Vector3 position, Vector3 velocity,
            Vector3 preferred, float radius, float maxSpeed)
        {
            if (!Enabled) return preferred;   // --no-avoid 负对照：与接线前逐字一致

            // 自身速度（米/tick）：只用于算 τ 视界对应的筛程（每 tick 一次 sqrt，n² 次比较前的一次性开销）。
            // 取 max(当前速度, 期望位移速度)——**刚生成/刚停下的智能体当前速度是 0**，只按它算筛程会把
            // 前方 8m 的不动玩家筛掉（实测：首个 tick 直冲过去、视界检查 0.0）。筛程要覆盖的是
            // "按意图走 τ 能到多远"，所以期望速度也必须算进来。
            float selfSpeed = (float)Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            float preferredSpeed = (float)Math.Sqrt(preferred.X * preferred.X + preferred.Z * preferred.Z);
            if (preferredSpeed > selfSpeed) selfSpeed = preferredSpeed;
            float horizon = _solver.TimeHorizon;

            int count = 0;
            for (int i = 0; i < _agents.Count; i++)
            {
                var a = _agents[i];
                if (a.Id == selfId) continue;
                if (excludeId.HasValue && a.Id == excludeId.Value) continue;

                float nx = a.X;
                float nz = a.Z;
                float dx = nx - position.X;
                float dz = nz - position.Z;
                // 筛程 = 半径和 + 近场余量 + τ 内双方的**最大**接近距离（|v_i|+|v_j| 是相对速度的
                // 上界，不用 sqrt 也算得出来——宁可多收几个非冲突邻居，不能漏掉 τ 内会撞上的）
                // 注：邻居的 a.Speed 是**上一 tick flush** 的值，刚起步的邻居会被低估一格（自己没有这个问题，
                // 上面 selfSpeed 取了 max(当前, 期望)）；下一 tick 自愈，且低估只会让筛程偏小、
                // 不会让人凭空绕路——不为此加兜底逻辑（审查裁定）。
                float reach = radius + a.Radius + NeighborMargin + (selfSpeed + a.Speed) * horizon;
                if (dx * dx + dz * dz > reach * reach) continue;

                if (dx == 0f && dz == 0f)
                {
                    // 退化 tie-break ①：**位置精确相同**——解算器在"同点 + 相对速度为零"时不产约束
                    // （见 OrcaSolver.CreateOrcaLine 的 wLength 分支）⇒ 会永久叠住。按 id 给一个
                    // **确定性**的 1e-4 偏移（不是随机数）：8 个罗盘方向之一，且对调双方得到相反方向，
                    // 于是双方各往对面那一侧让开。偏移量远小于任何真实半径，不影响正常几何。
                    TieBreak(selfId, a.Id, out float ox, out float oz);
                    nx += ox;
                    nz += oz;
                }
                else if (IsDegenerateHeadOn(dx, dz, velocity.X - a.VX, velocity.Z - a.VZ))
                {
                    // 退化 tie-break ②：**正面对撞且横向完全对称**（p 与相对速度共线到浮点噪声级）。
                    // 该走哪条锥腿（左边让还是右边让）这时完全由噪声决定——实测两个相向的机器人会挑到
                    // **同一绝对侧**，横向位移互相抵消、径直对穿（最小值 0.236m < 半径和 1.0）。
                    // 这里用确定性口径掰开：自身位置沿 p 的**固定手性**垂线偏 ε（= 邻居位置偏 −ε·perp）。
                    // 手性固定 ⇒ 双方的偏移在绝对坐标里相反（p 与垂线都随帧翻转）⇒ 各让一侧。
                    float invLen = 1f / (float)Math.Sqrt(dx * dx + dz * dz);
                    nx -= -dz * invLen * TieBreakOffset;
                    nz -= dx * invLen * TieBreakOffset;
                }
                if (count == MaxNeighbors) continue;   // 缓冲满：只丢写不下的这一个，继续扫（见 MaxNeighbors 注释）
                _neighborBuffer[count++] = new OrcaNeighbor(nx, nz, a.VX, a.VZ, a.Radius,
                    a.Participates ? OrcaSolver.ReciprocalResponsibility : OrcaSolver.FullResponsibility);
            }

            LastNeighborCount = count;
            if (count == 0)
            {
                // 护栏：没有邻居 ⇒ 逐字返回期望位移。**不能**依赖解算器内部的
                // "|v_pref| ≤ maxSpeed 时原样返回"分支——归一化误差可能让 |v_pref| 比 maxSpeed
                // 高几个 ulp，那就会走一次均匀缩短（除法+乘法）而丢掉逐位相等。
                return preferred;
            }

            var self = new OrcaAgent(position.X, position.Z, velocity.X, velocity.Z,
                radius, maxSpeed, preferred.X, preferred.Z);
            OrcaVelocity solved = _solver.Solve(self, _neighborBuffer.AsSpan(0, count));
            return new Vector3(solved.X, 0f, solved.Z);
        }

        /// <summary>退化 tie-break ① 的方向：k = (selfId − otherId) mod 8 → 45°×k 的单位向量。
        /// 对调双方得 k' = (8 − k) mod 8 → 方向相反（双方各自让开，不会同向挤）。</summary>
        private static void TieBreak(long selfId, long otherId, out float dx, out float dz)
        {
            int k = (int)(((selfId - otherId) % 8 + 8) % 8);
            double angle = k * (Math.PI / 4.0);
            dx = (float)Math.Cos(angle) * TieBreakOffset;
            dz = (float)Math.Sin(angle) * TieBreakOffset;
        }

        /// <summary>
        /// 退化判据（tie-break ②）：p 与相对速度**共线到浮点噪声级**且双方在接近。
        /// 符号：p = 邻居 − 自身、v_rel = 自身 − 邻居 ⇒ 接近 ⟺ v_rel 顺着 p（点积 &gt; 0）。
        /// 共线用**相对**判据（|p×v_rel| 与 |p||v_rel| 比）：噪声量级 ~1e-8 相对值，
        /// 真实几何错开 0.01m / 10m 也有 ~1e-3 相对值 —— 1e-5 把两者分得很开。
        /// </summary>
        private static bool IsDegenerateHeadOn(float dx, float dz, float relVx, float relVz)
        {
            float dot = dx * relVx + dz * relVz;
            if (dot <= 0f) return false;                                   // 不是在接近
            float cross = dx * relVz - dz * relVx;
            float pLenSq = dx * dx + dz * dz;
            float vLenSq = relVx * relVx + relVz * relVz;
            return cross * cross <= 1e-10f * pLenSq * vLenSq;
        }

        /// <summary>快照条目（结构体：重建 100 条不产生 GC）</summary>
        private struct SnapshotAgent
        {
            public long Id;
            public float X;
            public float Z;
            public float VX;
            public float VZ;
            public float Speed;   // |v|（米/tick）——只为算筛程，避免每次循环再开方
            public float Radius;
            public bool Participates;
        }
    }
}
