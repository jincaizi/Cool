using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpNet;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 客户端网络管理器 —— 连接/登录/进世界/断线重连闭环（P1 决策 D4：
    /// 重连 = 重新 Login 全流程；EnterWorld 平滑重连留 P2）。
    /// 由宿主每帧调用 Update；事件 OnEnterWorld/OnDisconnected 供上层接线。
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

        public State CurrentState { get; private set; } = State.Disconnected;
        public long LocalPlayerId { get; private set; }
        public string ZoneId { get; private set; } = string.Empty;
        public long ServerTickRateMs { get; private set; } = 67;
        public Vector3 SpawnPosition { get; private set; }

        public event Action? OnEnterWorld;
        public event Action<string>? OnDisconnected;
        public event Action<string>? OnError;

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
                    _remoteEntities.ApplySnapshot(entity);
                }
            }

            // 主线程消费离场通知（Object.Destroy 必须主线程，同快照封送机制）
            while (_pendingLeaves.TryDequeue(out var playerId))
            {
                _remoteEntities.Remove(playerId);
            }
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
