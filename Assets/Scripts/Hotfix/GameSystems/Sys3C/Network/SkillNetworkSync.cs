using System;
using UnityEngine;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 技能同步数据
    /// </summary>
    [Serializable]
    public struct SkillSyncData
    {
        public int SkillId;
        public SkillSubState SubState;
        public float StateElapsedTime;
        public float ChargeProgress;
        public long ServerTimestamp;
    }

    /// <summary>
    /// 技能同步消息
    /// </summary>
    [Serializable]
    public struct SkillSyncMessage
    {
        public long PlayerId;
        public SkillSyncData[] ActiveSkills;
        public int CurrentSkillId;
        public long Timestamp;
    }

    /// <summary>
    /// 技能同步组件 - 处理技能状态的网络同步
    /// </summary>
    public class SkillSyncComponent
    {
        private SkillCoordinator _skillCoordinator;
        private INetworkClient _networkClient;
        private long _playerId;
        private float _lastSyncTime;
        private const float SYNC_INTERVAL = 0.1f; // 100ms 同步间隔

        // 同步事件
        public event Action<SkillSyncData> OnRemoteSkillStateReceived;
        public event Action<int, SkillSubState> OnRemoteSkillStateChanged;

        // 缓冲的远程技能状态
        private readonly System.Collections.Generic.Queue<SkillSyncData> _pendingUpdates = new();
        private SkillSyncData _currentRemoteState;

        public SkillSyncComponent(INetworkClient networkClient, long playerId)
        {
            _networkClient = networkClient;
            _playerId = playerId;
        }

        /// <summary>
        /// 绑定技能协调器
        /// </summary>
        public void Bind(SkillCoordinator coordinator)
        {
            _skillCoordinator = coordinator;
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            _lastSyncTime += deltaTime;

            // 发送本地技能状态
            if (_lastSyncTime >= SYNC_INTERVAL && _skillCoordinator != null)
            {
                SendLocalSkillState();
                _lastSyncTime = 0f;
            }

            // 处理远程技能状态
            ProcessRemoteUpdates();
        }

        /// <summary>
        /// 发送本地技能状态到服务器
        /// </summary>
        private void SendLocalSkillState()
        {
            if (_networkClient == null || !_networkClient.IsConnected) return;

            var syncData = new SkillSyncData
            {
                SkillId = _skillCoordinator.CurrentSkill?.SkillId ?? 0,
                SubState = _skillCoordinator.CurrentSubState,
                StateElapsedTime = _skillCoordinator.CurrentSkill?.ElapsedTime ?? 0f,
                ChargeProgress = _skillCoordinator.CurrentSkill != null ? GetChargeProgress() : 0f,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };

            // 转换为网络消息格式
            var message = new SkillSyncMessage
            {
                PlayerId = _playerId,
                ActiveSkills = new SkillSyncData[] { syncData },
                Timestamp = DateTime.UtcNow.Ticks
            };

            SendSkillSyncMessage(message);
        }

        private float GetChargeProgress()
        {
            if (_skillCoordinator.CurrentSkill is SkillExecutor executor)
            {
                return executor.GetChargeProgress();
            }
            return 0f;
        }

        /// <summary>
        /// 发送技能同步消息
        /// </summary>
        private void SendSkillSyncMessage(SkillSyncMessage message)
        {
            // 通过 NetworkClient 发送
            // 这里需要实际的序列化实现
            Debug.Log($"[SkillSync] Sending sync: SkillId={message.ActiveSkills[0].SkillId}, State={message.ActiveSkills[0].SubState}");
        }

        /// <summary>
        /// 接收远程技能状态更新
        /// </summary>
        public void ReceiveRemoteSkillState(SkillSyncMessage message)
        {
            if (message.PlayerId == _playerId) return; // 忽略自己的消息

            foreach (var skillData in message.ActiveSkills)
            {
                _pendingUpdates.Enqueue(skillData);
            }
        }

        /// <summary>
        /// 处理远程更新
        /// </summary>
        private void ProcessRemoteUpdates()
        {
            while (_pendingUpdates.Count > 0)
            {
                var update = _pendingUpdates.Dequeue();
                ApplyRemoteSkillState(update);
            }
        }

        /// <summary>
        /// 应用远程技能状态
        /// </summary>
        private void ApplyRemoteSkillState(SkillSyncData remoteState)
        {
            if (remoteState.SkillId == 0)
            {
                // 远程技能结束
                Debug.Log($"[SkillSync] Remote skill ended");
                return;
            }

            // 检查状态变化
            if (_currentRemoteState.SkillId != remoteState.SkillId ||
                _currentRemoteState.SubState != remoteState.SubState)
            {
                Debug.Log($"[SkillSync] Remote state changed: SkillId={remoteState.SkillId}, State={remoteState.SubState}");
                OnRemoteSkillStateChanged?.Invoke(remoteState.SkillId, remoteState.SubState);
            }

            _currentRemoteState = remoteState;
            OnRemoteSkillStateReceived?.Invoke(remoteState);
        }

        /// <summary>
        /// 请求服务器中断本地技能
        /// </summary>
        public void RequestServerInterrupt(InterruptionSource source)
        {
            Debug.Log($"[SkillSync] RequestServerInterrupt: {source}");
            // 发送中断请求到服务器
            // 服务器处理后广播给其他客户端
        }

        /// <summary>
        /// 检查是否应该应用远程状态（乐观预测纠错）
        /// </summary>
        public bool ShouldApplyRemoteState(SkillSyncData remoteState, float serverTimeOffset)
        {
            // 如果远程时间比本地时间新，应该应用
            long remoteTime = remoteState.ServerTimestamp;
            long localTime = DateTime.UtcNow.Ticks - (long)(serverTimeOffset * 10000000);

            return remoteTime > localTime - 50000000; // 允许500ms误差
        }

        /// <summary>
        /// 估算服务器时间偏移
        /// </summary>
        public float EstimateServerTimeOffset(long serverTimestamp)
        {
            long localTime = DateTime.UtcNow.Ticks;
            long diff = serverTimestamp - localTime;
            return diff / 10000000f; // 转换为秒
        }
    }

    /// <summary>
    /// 技能事件消息（用于受击、技能释放等事件同步）
    /// </summary>
    [Serializable]
    public struct SkillEventMessage
    {
        public long PlayerId;
        public int SkillId;
        public SkillEventType EventType;
        public long Timestamp;

        // 事件数据
        public Vector3 TargetPosition;
        public int TargetEntityId;
    }

    public enum SkillEventType
    {
        SkillActivated,
        SkillCompleted,
        SkillInterrupted,
        HitConfirmed,
        BuffApplied
    }

    /// <summary>
    /// 技能事件处理器
    /// </summary>
    public class SkillEventHandler
    {
        private INetworkClient _networkClient;
        private long _playerId;

        // 事件回调
        public event Action<SkillEventMessage> OnSkillEventReceived;

        // 事件缓冲
        private readonly System.Collections.Generic.Queue<SkillEventMessage> _eventQueue = new();

        public SkillEventHandler(INetworkClient networkClient, long playerId)
        {
            _networkClient = networkClient;
            _playerId = playerId;
        }

        /// <summary>
        /// 发送技能事件
        /// </summary>
        public void SendSkillEvent(int skillId, SkillEventType eventType, Vector3 targetPosition = default, int targetEntityId = 0)
        {
            var message = new SkillEventMessage
            {
                PlayerId = _playerId,
                SkillId = skillId,
                EventType = eventType,
                Timestamp = DateTime.UtcNow.Ticks,
                TargetPosition = targetPosition,
                TargetEntityId = targetEntityId
            };

            Debug.Log($"[SkillEvent] Send: SkillId={skillId}, Type={eventType}");
            // 通过网络发送消息
        }

        /// <summary>
        /// 接收技能事件
        /// </summary>
        public void ReceiveSkillEvent(SkillEventMessage message)
        {
            if (message.PlayerId == _playerId) return;

            Debug.Log($"[SkillEvent] Received: SkillId={message.SkillId}, Type={message.EventType}");
            _eventQueue.Enqueue(message);
        }

        /// <summary>
        /// 处理事件队列
        /// </summary>
        public void Update()
        {
            while (_eventQueue.Count > 0)
            {
                var evt = _eventQueue.Dequeue();
                OnSkillEventReceived?.Invoke(evt);
            }
        }
    }

    /// <summary>
    /// 技能网络同步管理器 - 整合所有网络同步组件
    /// </summary>
    public class SkillNetworkManager
    {
        private readonly SkillSyncComponent _syncComponent;
        private readonly SkillEventHandler _eventHandler;
        private readonly NetworkBridge _networkBridge;
        private long _playerId;

        public SkillSyncComponent SyncComponent => _syncComponent;
        public SkillEventHandler EventHandler => _eventHandler;

        public SkillNetworkManager(NetworkBridge bridge, long playerId)
        {
            _networkBridge = bridge;
            _playerId = playerId;

            var networkClient = GetNetworkClient();
            _syncComponent = new SkillSyncComponent(networkClient, playerId);
            _eventHandler = new SkillEventHandler(networkClient, playerId);

            SetupCallbacks();
        }

        private INetworkClient GetNetworkClient()
        {
            // 从 NetworkBridge 获取网络客户端
            // 这里需要实际的实现
            return null;
        }

        private void SetupCallbacks()
        {
            _syncComponent.OnRemoteSkillStateChanged += (skillId, subState) =>
            {
                Debug.Log($"[SkillNetworkManager] Remote skill changed: {skillId} -> {subState}");
            };
        }

        /// <summary>
        /// 绑定技能协调器
        /// </summary>
        public void Bind(SkillCoordinator coordinator)
        {
            _syncComponent.Bind(coordinator);
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            _syncComponent.Update(deltaTime);
            _eventHandler.Update();
        }

        /// <summary>
        /// 发送技能激活事件
        /// </summary>
        public void SendSkillActivated(int skillId, Vector3 targetPosition)
        {
            _eventHandler.SendSkillEvent(skillId, SkillEventType.SkillActivated, targetPosition);
        }

        /// <summary>
        /// 发送命中确认事件
        /// </summary>
        public void SendHitConfirmed(int skillId, Vector3 hitPosition, int targetId)
        {
            _eventHandler.SendSkillEvent(skillId, SkillEventType.HitConfirmed, hitPosition, targetId);
        }

        /// <summary>
        /// 发送技能中断事件
        /// </summary>
        public void SendSkillInterrupted(int skillId, InterruptionSource source)
        {
            _syncComponent.RequestServerInterrupt(source);
        }
    }
}