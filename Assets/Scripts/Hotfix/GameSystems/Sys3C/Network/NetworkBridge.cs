using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 位置同步响应数据
    /// </summary>
    public struct PositionSyncResponseData
    {
        public uint AcknowledgedSequence;
        public long ServerTimestamp;
        public Vector3 AuthoritativePosition;
        public float AuthoritativeRotation;
        public bool HasPositionCorrection;
    }

    /// <summary>
    /// 位置同步请求数据
    /// </summary>
    public struct PositionSyncRequestData
    {
        public float X;
        public float Y;
        public float Z;
        public float Rotation;
        public float Speed;
        public long Timestamp;
        public uint Sequence;
    }

    /// <summary>
    /// 远程玩家同步数据（从AOT层传入）
    /// </summary>
    public struct RemotePlayerSyncData
    {
        public long PlayerId;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Speed;
        public long Timestamp;
    }

    /// <summary>
    /// 网络客户端接口 - Hotfix层定义，AOT层实现
    /// </summary>
    public interface INetworkClient
    {
        bool IsConnected { get; }
        event Action<RemotePlayerSyncData>? RemotePlayerSyncReceived;
        void SendPositionSync(PositionSyncRequestData request);
    }

    /// <summary>
    /// 网络桥接 — Hotfix层访问AOT KcpNet的唯一通道
    /// </summary>
    public class NetworkBridge
    {
        private INetworkClient? _networkClient;
        private Action<PositionSyncResponseData>? _onPositionSyncResponse;
        private Action<RemotePlayerSyncData>? _onRemotePlayerUpdate;
        private uint _localSequence;

        /// <summary>
        /// 初始化桥接（需要外部传入AOT层的网络客户端）
        /// </summary>
        public void Initialize(INetworkClient networkClient)
        {
            _networkClient = networkClient;
            _networkClient.RemotePlayerSyncReceived += OnRemotePlayerSyncReceived;
        }

        private void OnRemotePlayerSyncReceived(RemotePlayerSyncData data)
        {
            HandleRemotePlayerUpdate(data);
        }

        /// <summary>
        /// 发送本地位置同步请求
        /// </summary>
        public void SendPositionSync(Vector3 position, Quaternion rotation, float speed)
        {
            if (_networkClient == null || !_networkClient.IsConnected) return;

            var request = new PositionSyncRequestData
            {
                X = position.x,
                Y = position.y,
                Z = position.z,
                Rotation = rotation.eulerAngles.y,
                Speed = speed,
                Timestamp = DateTime.UtcNow.Ticks,
                Sequence = ++_localSequence
            };

            _networkClient.SendPositionSync(request);
        }

        /// <summary>
        /// 处理服务端位置同步响应
        /// </summary>
        public void HandlePositionSyncResponse(PositionSyncResponseData response)
        {
            _onPositionSyncResponse?.Invoke(response);
        }

        /// <summary>
        /// 处理服务端广播的其他玩家位置
        /// </summary>
        public void HandleRemotePlayerUpdate(RemotePlayerSyncData data)
        {
            _onRemotePlayerUpdate?.Invoke(data);
        }

        /// <summary>
        /// 注册位置同步响应回调
        /// </summary>
        public void RegisterPositionSyncCallback(Action<PositionSyncResponseData> callback)
        {
            _onPositionSyncResponse += callback;
        }

        /// <summary>
        /// 注册其他玩家位置更新回调
        /// </summary>
        public void RegisterRemotePlayerCallback(Action<RemotePlayerSyncData> callback)
        {
            _onRemotePlayerUpdate += callback;
        }

        /// <summary>
        /// 获取连接状态
        /// </summary>
        public bool IsConnected => _networkClient?.IsConnected ?? false;
    }
}