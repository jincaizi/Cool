using System;
using System.Collections.Generic;
using UnityEngine;
using KcpNet;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 玩家同步数据（服务端广播的他人位置）
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
    /// 网络桥接 — Hotfix层访问AOT KcpNet的唯一通道
    /// </summary>
    public class NetworkBridge
    {
        private KcpClient _kcpClient;
        private Action<PositionSyncResponse> _onPositionSyncResponse;
        private Action<RemotePlayerSyncData> _onRemotePlayerUpdate;
        private uint _localSequence;

        /// <summary>
        /// 初始化桥接（需要外部传入AOT层的KcpClient引用）
        /// </summary>
        public void Initialize(KcpClient kcpClient)
        {
            _kcpClient = kcpClient;

            // 注册消息处理器（通过AOT的MessageDispatcher）
            // 注意：这需要AOT层提供MessageDispatcher的访问接口
            // 或者通过事件派发的方式让Hotfix订阅
        }

        /// <summary>
        /// 发送本地位置同步请求
        /// </summary>
        public void SendPositionSync(Vector3 position, Quaternion rotation, float speed)
        {
            if (_kcpClient == null || !_kcpClient.IsConnected) return;

            var request = new PositionSyncRequest
            {
                X = position.x,
                Y = position.y,
                Z = position.z,
                Rotation = rotation.eulerAngles.y,
                Speed = speed,
                Timestamp = DateTime.UtcNow.Ticks,
                Sequence = ++_localSequence
            };

            _kcpClient.SendAsync(request, MessageFlags.Reliable).ConfigureAwait(false);
        }

        /// <summary>
        /// 处理服务端位置同步响应
        /// </summary>
        public void HandlePositionSyncResponse(PositionSyncResponse response)
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
        public void RegisterPositionSyncCallback(Action<PositionSyncResponse> callback)
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
        public bool IsConnected => _kcpClient?.IsConnected ?? false;
    }
}