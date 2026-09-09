using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpNet;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 客户端网络管理器 —— 连接/登录/进世界/断线重连闭环（P1 决策 D4：
    /// 重连 = 重新 Login 全流程）。由宿主每帧调用 Update；
    /// 事件 OnEnterWorld/OnDisconnected 供上层接线。
    /// 注：EnterWorld 平滑重连（保留现场不重登）原 P1 注释标"留 P2"但 P2 未承接——
    /// 现为 P3 backlog 项（见 plan 偏差记录），勿按旧注释期待该能力。
    /// </summary>
    public sealed class ClientNetworkManager : IDisposable
    {
        public enum State { Disconnected, Connecting, LoggingIn, InWorld }

        private readonly INetworkClient _client;
        private readonly MessageDispatcher _dispatcher = new MessageDispatcher();
        private readonly ClockSyncDriver _clockSync;
        private readonly RemoteEntityRegistry _remoteEntities;
        private readonly string _username;
        private readonly System.Collections.Concurrent.ConcurrentQueue<WorldSnapshot> _pendingSnapshots =
            new System.Collections.Concurrent.ConcurrentQueue<WorldSnapshot>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<long> _pendingLeaves =
            new System.Collections.Concurrent.ConcurrentQueue<long>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<PositionSyncResponse> _pendingResponses =
            new System.Collections.Concurrent.ConcurrentQueue<PositionSyncResponse>();
        private MovementReporter? _reporter;

        public State CurrentState { get; private set; } = State.Disconnected;
        public long LocalPlayerId { get; private set; }
        public string ZoneId { get; private set; } = string.Empty;
        public long ServerTickRateMs { get; private set; } = 67;
        /// <summary>
        /// 时钟平滑偏移（ticks）——暴露 smoothed offset（T6 抖动实测读）；
        /// 插值渲染实际经 ToServerTime（时钟算法的 min-filter offset）驱动，
        /// 两者数值不同步，勿混淆。
        /// </summary>
        public long ClockSmoothedOffsetTicks => _clockSync.SmoothedOffsetTicks;
        public Vector3 SpawnPosition { get; private set; }
        public MovementReporter? Reporter => _reporter;

        public event Action? OnEnterWorld;
        public event Action<string>? OnDisconnected;
        public event Action<string>? OnError;
        public event Action<Vector3, Quaternion>? OnCorrectionNeeded;

        public ClientNetworkManager(INetworkClient client, RemoteEntityRegistry remoteEntities, string username)
        {
            _client = client;
            _remoteEntities = remoteEntities;
            _username = username;

            _clockSync = new ClockSyncDriver(
                req => _client.Send(MessageId.ClockSyncRequest, req));

            _dispatcher.Subscribe(MessageId.LoginResponse, OnLoginResponse);
            _dispatcher.Subscribe(MessageId.ClockSyncResponse, OnClockSyncResponse);
            _dispatcher.Subscribe(MessageId.WorldSnapshot, OnWorldSnapshot);
            _dispatcher.Subscribe(MessageId.PlayerLeaveRoom, OnPlayerLeave);

            _client.MessageReceived += _dispatcher.Dispatch;
            _client.Disconnected += OnSocketDisconnected;
        }

        /// <summary>
        /// 装配移动上报（进世界后由宿主调用；传入本地角色数据源）。
        /// speedProvider 必须反映真实移动速度（超速钳制链路依赖它——固定值会使纠偏测不到）。
        /// 宿主接线：manager.OnCorrectionNeeded += (pos, rot) => policy.ApplyServerCorrection(pos, rot);
        /// </summary>
        public void AttachMovementReporter(Func<Cool.SyncCore.Vec3> positionProvider,
            Func<float> rotationProvider, Func<float> speedProvider, int injectedSendDelayMs = 0)
        {
            if (_reporter != null)
            {
                // 重复装配守卫：重连 = 全新 InWorld 流程（P1 D4），但防御双订阅——
                // 二次 attach 会双订阅 dispatcher + 旧会话残留响应污染新会话对账
                return;
            }

            _reporter = new MovementReporter(_client, positionProvider, rotationProvider, speedProvider, injectedSendDelayMs);
            _dispatcher.Subscribe(MessageId.PositionSyncResponse, OnPositionSyncResponse);
        }

        public async void ConnectAndLogin(string host, int port)
        {
            CurrentState = State.Connecting;
            var ok = await _client.ConnectAsync(host, port);
            if (!ok)
            {
                OnError?.Invoke("connect failed");
                CurrentState = State.Disconnected;
                return;
            }

            CurrentState = State.LoggingIn;
            _client.Send(MessageId.LoginRequest, new LoginRequest
            {
                Username = _username,
                PasswordHash = "md5_hash_placeholder",
                ZoneId = "Global",
                ClientVersion = "1.0.0",
                DeviceInfo = "P1",
                ProtocolVersion = ProtocolConstants.ProtocolVersion
            });
        }

        public void Update(float deltaSeconds)
        {
            if (CurrentState != State.InWorld) return;

            _clockSync.Tick(deltaSeconds);

            // 主线程消费快照（RemoteEntityRegistry 的 Unity API 必须主线程）
            while (_pendingSnapshots.TryDequeue(out var snapshot))
            {
                foreach (var entity in snapshot.Entities)
                {
                    if (entity.PlayerId == LocalPlayerId) continue; // 本地玩家跳过
                    _remoteEntities.ApplySnapshot(entity, snapshot.ServerTimestamp);
                }
            }

            // 主线程消费离场通知（Object.Destroy 必须主线程，同快照封送机制）
            while (_pendingLeaves.TryDequeue(out var playerId))
            {
                _remoteEntities.Remove(playerId);
            }

            // 纠偏响应封送（主线程对账 + 事件——PredictionReconciler 非线程安全，见类注释）
            DrainResponses();

            // 上报节流（与测试宿主解耦：由 manager 驱动，非 policy）
            _reporter?.Tick(deltaSeconds, (float)ServerTickRateMs / 1000f);

            // 插值渲染（服务器时钟基准：ClockSyncDriver.ToServerTime(本地墙钟) − 2 ticks 延迟）。
            // 收敛前（首个 ClockSyncResponse 前）ToServerTime 以本地墙钟当服务器钟（偏移 0），
            // spawn 瞬间短暂走 newest/oldest fallback，突发收敛（~1.6s）后正常——属预期退化，非缺陷
            long nowServerTicks = _clockSync.ToServerTime(System.DateTime.UtcNow.Ticks);
            long renderDelayTicks = ServerTickRateMs * 10_000L * 2; // 2 ticks（ServerTickRateMs 单位 ms）
            _remoteEntities.UpdateRender(nowServerTicks, renderDelayTicks);

            // P3 AOI：缺席超时清理（视野外实体停流——1.2s 无快照销毁）。
            // 时钟收敛前跳过：ToServerTime 未收敛时以本地墙钟充当服务器钟（偏移 0），
            // 本机时钟快 >1.2s 会瞬间清空全部视图——收敛（~1.6s 突发）后才安全
            if (_clockSync.HasConverged)
                _remoteEntities.UpdateExpiry(nowServerTicks);
        }

        private void OnLoginResponse(IMessage message)
        {
            var response = (LoginResponse)message;
            if (!response.Success)
            {
                OnError?.Invoke($"login rejected: {response.Message}");
                CurrentState = State.Disconnected;
                return;
            }

            LocalPlayerId = response.PlayerId;
            ZoneId = response.ZoneId;
            ServerTickRateMs = response.ServerTickRateMs > 0 ? response.ServerTickRateMs : 67;
            SpawnPosition = new Vector3(response.SpawnX, response.SpawnY, response.SpawnZ);
            CurrentState = State.InWorld;

            // 时钟采样突发从进世界才开始（Update 仅在 InWorld 驱动 Tick）
            _clockSync.StartBurst();

            // 出生点定位（本地玩家表现接线留 P2；P1 以事件通知上层）
            OnEnterWorld?.Invoke();
        }

        private void OnClockSyncResponse(IMessage message)
        {
            _clockSync.OnResponse((ClockSyncResponse)message);
        }

        private void OnWorldSnapshot(IMessage message)
        {
            // 回调线程（网络驱动）：只入队，Unity API 消费留主线程 Update
            _pendingSnapshots.Enqueue((WorldSnapshot)message);
        }

        private void OnPositionSyncResponse(IMessage message)
        {
            // 回调线程（网络驱动）：只入队——对账字典（PredictionReconciler._frames）与主线程
            // RecordFrame 并发会竞态；且纠偏事件会触发 Unity Transform 写（必须主线程）。
            // 纪律同 WorldSnapshot 封送（OnWorldSnapshot 同款模式）。
            _pendingResponses.Enqueue((PositionSyncResponse)message);
        }

        /// <summary>主线程消费纠偏响应（Update 内、上报 Tick 前调用）</summary>
        private void DrainResponses()
        {
            while (_pendingResponses.TryDequeue(out var response))
            {
                if (_reporter != null && _reporter.OnResponse(response))
                {
                    // 需要纠偏 → 上层经 NetMovementPolicy.ApplyServerCorrection 执行平滑拉回
                    OnCorrectionNeeded?.Invoke(
                        new Vector3(response.AuthoritativeX, response.AuthoritativeY, response.AuthoritativeZ),
                        Quaternion.Euler(0f, response.AuthoritativeRotation, 0f));
                }
            }
        }

        private void OnPlayerLeave(IMessage message)
        {
            // 回调线程（网络驱动）只入队；Unity API 消费留主线程 Update
            _pendingLeaves.Enqueue(((PlayerLeaveRoom)message).PlayerId);
        }

        private void OnSocketDisconnected(string reason)
        {
            CurrentState = State.Disconnected;
            OnDisconnected?.Invoke(reason);
        }

        public void Dispose()
        {
            _client.MessageReceived -= _dispatcher.Dispatch;
            _client.Disconnected -= OnSocketDisconnected;
            _remoteEntities.Clear();
            _client.Dispose();
        }
    }
}
