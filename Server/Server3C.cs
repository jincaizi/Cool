using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KcpServer.Monster; // P5：怪物模块（EnableMonsters/tick 接线）
using KcpServer.Movement; // 2026-10-13：ORCA 避让（AvoidanceSystem / AvoidanceProbe）

namespace KcpServer
{
    public sealed class Server3C : IDisposable
    {
        private readonly ILogger _logger;
        private readonly RoomManager _roomManager;
        private readonly LoginHandler _loginHandler;
        private readonly PositionSyncHandler _positionSyncHandler;
        private readonly EchoHandler _echoHandler;
        private readonly ClockSyncHandler _clockSyncHandler;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private Socket? _udpServer;
        private Task? _receiveTask;
        private Task? _cleanupTask;
        private bool _disposed;

        private readonly SessionManager _sessionManager;
        private readonly ConcurrentDictionary<long, long> _sessionIdToPlayerId = new ConcurrentDictionary<long, long>();
        private readonly ConcurrentDictionary<long, long> _playerIdToSessionId = new ConcurrentDictionary<long, long>();
        private readonly ConcurrentDictionary<long, IServerSession> _playerIdToSession = new ConcurrentDictionary<long, IServerSession>();

        // 已接线会话簿记（RegisterSession 幂等 + 断开释放，防无界增长）。
        // 锁：RegisterSession 由 WS 监听器的接受连接任务调用（非主循环线程），断开路径同锁释放
        private readonly object _registerLock = new object();
        private readonly HashSet<long> _registeredSessions = new HashSet<long>();

        /// <summary>
        /// 主循环 tick 间隔（~15Hz）。**GameLoop 构造与复活计时（D-2）共用同一个值**——
        /// 两处各写一个 66 是复活时长随主循环调速静默漂移的隐患
        /// </summary>
        private const int TickIntervalMs = 66;

        // R2：唯一消费者线程独占世界状态；IO 线程只 Enqueue，不触碰状态
        private readonly GameLoop<(EndPoint endpoint, byte[] data)> _gameLoop;

        // P3：per-observer 分级广播（AoiGrid 视野 + 带节拍/静止保活；主循环独占访问）
        private readonly EntityStore _entityStore = new EntityStore();
        private readonly AoiBroadcaster _aoi = new AoiBroadcaster();

        // P4：服务端权威战斗裁决（校验/命中/掷骰/扣血；广播经 CastHandler）
        private readonly CastHandler _castHandler;

        // P5：服务端权威怪物（AI 决策 + 死亡重生；攻击裁决经 CombatResolver、广播经 CastHandler——T3）
        private MonsterModule? _monsterModule;
        private long _monsterIdSeed = MonsterModule.NpcIdSeed;

        private BotRunner? _botRunner;

        // 2026-10-13：ORCA 避让（怪物 + 机器人共用一份世界快照；玩家只当障碍）。
        // 一份实例就够——主循环单线程，解算器**非线程安全**（持有复用缓冲）。
        private readonly AvoidanceSystem _avoidance;
        private AvoidanceProbe? _avoidanceProbe;

        /// <summary>诊断计数：5s 窗口内真实发送的 WorldSnapshot 消息数（与实体发送数区分——集成定位用）</summary>
        private long _snapshotMessagesSent;

        /// <summary>压测模式：注册 N 个 bot（无 session 假玩家）并在主循环 tick 推进。
        /// 必须在 StartAsync 之前调用（bot 注册写 EntityStore——R2 主循环独占）</summary>
        public void EnableBots(int count)
        {
            long botIdSeed = 1_000_000;
            _botRunner = new BotRunner(_entityStore, () => System.Threading.Interlocked.Increment(ref botIdSeed),
                avoidance: _avoidance);
            _botRunner.Spawn(count);
        }

        /// <summary>怪物模式：注册 N 个怪物（固定布局——T5 e2e 依赖）。必须在 StartAsync 之前调用</summary>
        public void EnableMonsters(int count)
        {
            _monsterModule = new MonsterModule(_entityStore, () => Interlocked.Increment(ref _monsterIdSeed),
                avoidance: _avoidance);
            _monsterModule.SpawnDefaultLayout(count);
        }

        /// <summary>ORCA 避让负对照（CLI <c>--no-avoid</c>）：关掉后智能体按愿望位移直行。
        /// 必须在 StartAsync 之前调用（主循环独占世界状态——R2）。</summary>
        public void DisableAvoidance() => _avoidance.Enabled = false;

        /// <summary>活体对照探针（CLI <c>--probe</c>）：每 tick 统计全体实体的最小两两间距并定期打日志。
        /// 纯诊断（O(n²) 距离比较，只在显式打开时跑），必须在 StartAsync 之前调用。</summary>
        public void EnableDistanceProbe() => _avoidanceProbe = new AvoidanceProbe(_entityStore, _avoidance);

        // 超时配置
        private readonly TimeSpan _sessionTimeout = TimeSpan.FromSeconds(300);
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(30);

        public RoomManager RoomManager => _roomManager;
        public int PlayerCount => _roomManager.TotalPlayerCount;
        public int ActiveSessionCount => _sessionManager.SessionCount;

        /// <summary>当前主循环 tick（packet 处理时比 lambda 已推进值落后一个迭代——冷却记录与判定同基准，语义不受影响；勿'修正'为实时值）</summary>
        public uint CurrentTick { get; private set; }

        public Server3C(ILogger logger)
        {
            _logger = logger;
            _sessionManager = new SessionManager(logger);
            _roomManager = new RoomManager(new RoomManagerConfig());
            _loginHandler = new LoginHandler(this, _roomManager, _logger);
            _positionSyncHandler = new PositionSyncHandler(this, _logger, _entityStore);
            _echoHandler = new EchoHandler(this, _logger);
            _clockSyncHandler = new ClockSyncHandler(this, _logger);
            _castHandler = new CastHandler(this, _logger, new CombatResolver(_entityStore));

            // 避让系统在 EnableMonsters 之前就可能建（--bots 单独跑）；模板表由模块在启用时补上，
            // 所以半径解析走**延迟查**（闭包读字段，调用时模块已就位）；未启用怪物时也走不到 Npc 分支
            _avoidance = new AvoidanceSystem(_entityStore,
                templateId => _monsterModule?.RadiusOfTemplate(templateId) ?? AvoidanceSystem.DefaultMonsterRadius,
                secondsPerTick: TickIntervalMs / 1000f);

            // R2：唯一消费者线程独占世界状态；handler 同步链内完成解码/分发/发送
            _gameLoop = new GameLoop<(EndPoint, byte[])>(
                TimeSpan.FromMilliseconds(TickIntervalMs),   // ~15Hz
                packet => HandlePacket(packet.Item1, packet.Item2),
                logger,
                tick =>
                {
                    CurrentTick = (uint)tick;

                    // ORCA（2026-10-13）：**tick 起始处**拍一份位置+速度快照（Jacobi）——
                    // 机器人 tick 与怪物 tick 都对着同一份解算，先解谁后解谁不影响结果。
                    // 智能体各自就地积分：它们读的是快照，所以顺序不改变别人的输入。
                    _avoidance.EnsureSnapshot((uint)tick);
                    _avoidanceProbe?.Sample((uint)tick);

                    _botRunner?.Tick((uint)tick); // P3 压测：bot 群每 tick 游走

                    // P5：怪物 AI tick → 攻击裁决 drain（同 tick 冷却记录即时生效）→ 死亡移除清 AOI
                    if (_monsterModule != null)
                    {
                        _monsterModule.Tick((uint)tick);
                        foreach (var (npcId, request) in _monsterModule.DrainPendingCasts())
                        {
                            var resolution = _castHandler.Resolve(npcId, request, (uint)tick);
                            _ = _castHandler.HandleNpcCastAsync(npcId, resolution); // 拒绝分支内部处理（日志+计数）——审计口径须可证伪
                        }
                        foreach (var npcId in _monsterModule.DrainRemovedNpcs())
                            _aoi.RemoveEntity(npcId);
                    }

                    // P8：死亡计时与复活（D-2，8 秒）。客户端不自判死（F10），
                    // 所以这里是联网下唯一的复活路径。放在怪物 tick 之后——本 tick 由怪物
                    // 打死的实体即时打上死亡标记，不必等下一 tick
                    TickRespawns((uint)tick);

                    // 每 5s（75 tick）带宽统计日志——T7 验收口径（记录非断言）
                    if (tick % 75 == 0)
                    {
                        _logger.LogInformation($"[Aoi] 5s stat: sends/s={_aoi.TotalEntitySends / 5f:F0}, est_bytes/s={_aoi.TotalEntitySends * 33f / 5f:F0}, snapshotMsgs/s={_snapshotMessagesSent / 5f:F0}"); // 33B/entity 实测（T5）
                        _aoi.TotalEntitySends = 0;
                        _snapshotMessagesSent = 0;
                    }

                    BroadcastWorldSnapshot((uint)tick);
                });
        }

        public Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Server3C));

            _udpServer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpServer.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpServer.Bind(new IPEndPoint(IPAddress.Any, port));

            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
            _gameLoop.Start();
            _cleanupTask = Task.Run(() => CleanupLoop(_cts.Token), _cts.Token);

            _logger.LogInformation($"Server3C started on port {port} with KCP protocol");
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_udpServer == null) return;

            _logger.LogInformation("Stopping Server3C...");

            _cts.Cancel();

            // 关闭所有会话
            foreach (var s in _sessionManager.AllSessions)
            {
                await s.CloseAsync("ServerShutdown");
                s.Dispose();
            }

            if (_receiveTask != null)
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));

            await _gameLoop.Stop();

            if (_cleanupTask != null)
                await _cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));

            _udpServer.Close();
            _udpServer = null;

            _logger.LogInformation("Server3C stopped");
        }

        public void BindPlayerToSession(long playerId, IServerSession session)
        {
            _playerIdToSessionId[playerId] = session.SessionId;
            _sessionIdToPlayerId[session.SessionId] = playerId;
            _playerIdToSession[playerId] = session;
        }

        public void UnbindPlayer(long playerId)
        {
            if (_playerIdToSessionId.TryRemove(playerId, out var sessionId))
            {
                _sessionIdToPlayerId.TryRemove(sessionId, out _);
            }
            _playerIdToSession.TryRemove(playerId, out _);
        }

        public void RegisterEntity(long playerId, string playerName)
        {
            var player = _entityStore.AddPlayer(playerId, playerName);
            // P5：登录标志置在 store 实例上——怪物感知（MonsterModule.DetectTarget）与
            // 怪物施法候选（CastHandler）过滤读的是本实例；LoginHandler 自建实例只进 RoomManager。
            // BotRunner 不经此路径（直接 AddPlayer）——压测 bot 保持非登录态（D-P5-3 语义）
            player.IsLoggedIn = true;
            // AOI：实体立即可见靠 AoiGrid 首帧 + AoiBandScheduler HasSent=false（无需标脏）——
            // MarkDirty 保留仅为 P2 测试语义（PositionSyncEntityStoreTests 断言 IsDirty）
            _entityStore.MarkDirty(player);
        }

        public void UnregisterEntity(long playerId)
        {
            _entityStore.RemovePlayer(playerId);
            _aoi.RemoveEntity(playerId); // 注销清 AOI 状态：同 id 重登无残留视野迟滞/节拍
        }

        /// <summary>观察者会话断开——清理其 AOI 视野/节拍状态</summary>
        public void RemoveObserver(long playerId) => _aoi.RemoveObserver(playerId);

        /// <summary>观察者当前 AOI 可见实体（CastHandler 广播范围）</summary>
        public List<PlayerState> GetVisiblePlayers(long observerId) => _aoi.GetVisible(observerId, _entityStore);

        /// <summary>能看到指定实体的在线观察者（P5 怪物 CastBroadcast 范围——AoiGrid 迟滞视野复用；
        /// O(N²) 每施法一次（外层遍历 × 每观察者 GetVisible O(N)）——怪物规模小可接受；实体量大时换反向索引（注释留痕））</summary>
        public List<long> GetObserversOf(long entityId)
        {
            var result = new List<long>();
            foreach (var observer in _entityStore.All)
            {
                if (observer.Kind != (byte)EntityKind.Player) continue;
                if (!_playerIdToSession.ContainsKey(observer.PlayerId)) continue;
                var visible = _aoi.GetVisible(observer.PlayerId, _entityStore);
                foreach (var e in visible)
                    if (e.PlayerId == entityId) { result.Add(observer.PlayerId); break; }
            }
            return result;
        }

        public long? GetPlayerId(IServerSession session)
        {
            return _sessionIdToPlayerId.TryGetValue(session.SessionId, out var playerId) ? playerId : null;
        }

        public IServerSession? GetSession(long playerId)
        {
            return _playerIdToSession.TryGetValue(playerId, out var session) ? session : null;
        }

        public Task SendToSessionAsync(IServerSession session, MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
        {
            if (_udpServer == null || !session.IsConnected)
                return Task.CompletedTask;

            try
            {
                session.Send(messageId, message, flags);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send message to session {session.SessionId}");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 会话 id 发号源（转发 SessionManager）—— WS 监听器建会话时取号。
        /// 必须与 KCP 共用同一个计数器：_sessionIdToPlayerId 与 RegisterSession 的幂等簿记
        /// 都按 SessionId 键控，两条传输各自发号必然撞号（详见 SessionManager.NextSessionId）
        /// </summary>
        public long NextSessionId() => _sessionManager.NextSessionId();

        /// <summary>
        /// 注册一个会话到事件链 —— 传输无关。
        /// KCP 路径在 UDP 收包处内联接线（不属本次改动范围），
        /// WebSocket 监听器建完会话后调用本方法。
        /// 幂等接线：同一会话重复注册只生效一次（消除发布-订阅窗口竞态）——与 KCP 路径 EnsureWired 同语义。
        /// 不幂等会让 DataReceived 挂两遍 → 每个包被 ProcessPacket 处理两次（登录建两个 PlayerState、
        /// 施法裁决两次）——是静默的状态污染，不是异常。簿记在 OnSessionDisconnected 释放。
        /// 入站包经 OnSessionDataReceivedEnqueued 投递到主循环 —— 本入口的调用方不是主循环线程。
        /// </summary>
        public void RegisterSession(IServerSession session)
        {
            lock (_registerLock)
            {
                if (!_registeredSessions.Add(session.SessionId)) return;

                session.DataReceived += OnSessionDataReceivedEnqueued;
                session.Disconnected += OnSessionDisconnected;
                session.Error += OnSessionError;
            }
        }

        /// <summary>
        /// 每 tick 扫一遍实体：新死的打标记，到点的复活并**给本人**发通知。
        /// 复杂度 O(实体数)/tick——与既有的 AOI 广播同量级（本 tick 的第二遍遍历，无新增数据结构）。
        /// **只改存活与血量，不摆位置**：玩家位置由客户端上报（D-N5），服务端写会被下一次上报覆盖。
        /// </summary>
        private void TickRespawns(uint tick)
        {
            foreach (var p in _entityStore.All)
            {
                if (!p.IsAlive)
                {
                    p.MarkDead(tick);
                    continue;
                }
                if (!p.TryRespawn(tick, TickIntervalMs)) continue;

                // 只发给**有会话**的实体（bot 没有 session，没人可通知）
                if (!p.IsLoggedIn) continue;
                var session = GetSession(p.PlayerId);
                if (session == null) continue;
                _ = SendToSessionAsync(session, MessageId.RespawnNotice,
                    new RespawnNotice { Hp = p.Hp, Tick = tick });
            }
        }

        /// <summary>每 tick 为各在线 observer 组装并广播其 AOI 可见快照（带节拍/静止保活）</summary>
        private void BroadcastWorldSnapshot(uint tick)
        {
            foreach (var observer in _entityStore.All)
            {
                if (!_playerIdToSession.TryGetValue(observer.PlayerId, out var session)) continue;

                var entities = _aoi.BuildForObserver(observer, tick, _entityStore);
                _aoi.TotalEntitySends += entities.Count; // T7 带宽统计累计（5s 日志后归零）
                if (entities.Count == 0) continue; // 视野空：不发
                _snapshotMessagesSent++; // 真实发送的 WorldSnapshot 消息数（5s 日志后归零——集成诊断）

                var observerSnapshot = new WorldSnapshot
                {
                    Tick = tick,
                    ServerTimestamp = ServerClock.NowTicks(),
                    Entities = entities
                };
                session.Send(MessageId.WorldSnapshot, observerSnapshot);
            }
        }

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Receive loop started");

            var buffer = new byte[65536];
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (!cancellationToken.IsCancellationRequested && _udpServer != null)
            {
                try
                {
                    if (_udpServer.Available > 0)
                    {
                        var bytesRead = _udpServer.ReceiveFrom(buffer, ref remoteEndPoint);
                        if (bytesRead > 0)
                        {
                            var data = new byte[bytesRead];
                            Array.Copy(buffer, 0, data, 0, bytesRead);
                            _gameLoop.Enqueue((remoteEndPoint, data));
                        }
                    }
                    else
                    {
                        await Task.Delay(1, cancellationToken);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset)
                {
                    // UDP 端口不可达（ICMP）——静默忽略（P0 终审 SHOULD 项）
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in receive loop");
                }
            }

            _logger.LogInformation("Receive loop stopped");
        }

        private void HandlePacket(EndPoint remoteEndPoint, byte[] data)
        {
            try
            {
                var session = _sessionManager.GetOrCreateSession(remoteEndPoint, data);
                if (session == null) return;

                if (session.SendSocket == null)
                    session.SetSendSocket(_udpServer!);

                // 幂等接线：事件订阅先于任何包处理（消除发布-订阅窗口竞态）
                session.EnsureWired(() =>
                {
                    session.DataReceived += OnSessionDataReceived;
                    session.Disconnected += OnSessionDisconnected;
                    session.Error += OnSessionError;
                });

                session.HandleUdpPacket(data, data.Length);
                session.UpdateActivity();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling packet");
            }
        }

        private void OnSessionDataReceived(object? sender, byte[] data)
        {
            if (sender is IServerSession session)
            {
                ProcessPacket(session, data);
            }
        }

        /// <summary>
        /// RegisterSession 路径的入站包处理器 —— 与 KCP 内联路径（OnSessionDataReceived）的差别
        /// **只在哪个线程**：KCP 的 DataReceived 由主循环线程的 HandlePacket 触发，本入口由 WS 接收
        /// 循环（线程池线程）触发。故此处只入队，处理交给唯一消费者线程（R2）——
        /// 直接在 IO 线程上 ProcessPacket 会给世界状态开出第二条不受游戏循环管辖的并发入口
        /// （主循环正在遍历 EntityStore/AOI 时被改）。两条路径各自保持自己的语义，终点都在主循环。
        /// </summary>
        private void OnSessionDataReceivedEnqueued(object? sender, byte[] data)
        {
            if (sender is IServerSession session)
            {
                _gameLoop.EnqueueAction(() => { _ = ProcessPacket(session, data); });
            }
        }

        private async Task ProcessPacket(IServerSession session, byte[] data)
        {
            try
            {
                var (messageId, message) = MessageCodec.Decode(data);
                session.UpdateActivity();

                switch (message)
                {
                    case LoginRequest login:
                        await _loginHandler.HandleLoginAsync(session, login);
                        break;

                    case PositionSyncRequest posSync:
                        await _positionSyncHandler.HandlePositionSyncAsync(session, posSync);
                        break;

                    case EchoRequest echoRequest:
                        await _echoHandler.HandleEchoAsync(session, echoRequest);
                        break;

                    case ClockSyncRequest clockSync:
                        await _clockSyncHandler.HandleClockSyncAsync(session, clockSync);
                        break;

                    case CastRequest cast:
                        await _castHandler.HandleCastAsync(session, cast);
                        break;

                    case Heartbeat heartbeat:
                        _logger.LogDebug($"Heartbeat from session {session.SessionId}: seq={heartbeat.Sequence}");
                        // 回应心跳
                        await SendToSessionAsync(session, MessageId.Heartbeat, heartbeat);
                        break;

                    default:
                        _logger.LogWarning($"Unknown message type: {messageId} from session {session.SessionId}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing packet from session {session.SessionId}");
            }
        }

        private void OnSessionDisconnected(object? sender, EventArgs e)
        {
            if (sender is IServerSession session)
            {
                _logger.LogInformation($"Session {session.SessionId} disconnected");
                // 释放接线簿记并退订（防无界增长；同一会话对象断开后重新注册仍是干净的）——
                // 簿记不是世界状态，就地释放，不经主循环
                lock (_registerLock)
                {
                    _registeredSessions.Remove(session.SessionId);
                    session.DataReceived -= OnSessionDataReceived;          // KCP 内联路径
                    session.DataReceived -= OnSessionDataReceivedEnqueued;  // RegisterSession 路径
                    session.Disconnected -= OnSessionDisconnected;
                    session.Error -= OnSessionError;
                }
                // 会话对象回收：断开是终态，此后无人在它身上收发。
                // 不做的话 WS 会话的 drain 轮询（Task.Delay(2)）与 socket 会活到进程结束——
                // 每次断开漏一个 500Hz 空转 + 一个未释放句柄（服务端长期运行时无界增长）。
                // Dispose 幂等：KCP 路径既有的 StopAsync/CleanupLoop 再调一次是无害的。
                session.Dispose();
                // R2：世界状态变更一律经主循环动作队列
                _gameLoop.EnqueueAction(() => { _ = _loginHandler.HandleDisconnectAsync(session); });
                _gameLoop.EnqueueAction(() => _sessionManager.RemoveById(session.SessionId));
            }
        }

        private void OnSessionError(object? sender, Exception ex)
        {
            if (sender is IServerSession session)
            {
                _logger.LogError(ex, $"Session {session.SessionId} error");
            }
        }

        private async Task CleanupLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                    // 清理超时会话
                    var timeoutSessions = new System.Collections.Generic.List<IServerSession>();
                    foreach (var s in _sessionManager.AllSessions)
                    {
                        if (s.IsTimedOut(_sessionTimeout))
                        {
                            timeoutSessions.Add(s);
                        }
                    }

                    foreach (var s in timeoutSessions)
                    {
                        _logger.LogInformation($"Session {s.SessionId} timed out");
                        // 断开清理统一走事件路径（CloseAsync 同步触发 Disconnected → OnSessionDisconnected 入队），避免双执行
                        await s.CloseAsync("Timeout");
                        s.Dispose();
                    }

                    // 清理空房间（R2：经主循环）
                    _gameLoop.EnqueueAction(() => _roomManager.CleanupEmptyRooms());

                    if (PlayerCount > 0)
                    {
                        _logger.LogInformation($"Server stats - Players: {PlayerCount}, Sessions: {ActiveSessionCount}, Rooms: {_roomManager.RoomCount}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in cleanup loop");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cts.Cancel();

            // 停掉主循环（不走 StopAsync 直接 Dispose 时防止循环线程泄漏）
            _gameLoop.Dispose();

            // SessionManager 内部 Dispose 各会话（事件退订随会话引用一并释放）
            _sessionManager.Dispose();

            _cts.Dispose();
        }
    }
}