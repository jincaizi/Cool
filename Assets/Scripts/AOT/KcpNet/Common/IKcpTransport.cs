using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// KCP传输层接口
    /// </summary>
    public interface IKcpTransport : IDisposable
    {
        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 远程终结点
        /// </summary>
        EndPoint? RemoteEndPoint { get; }

        /// <summary>
        /// 本地终结点
        /// </summary>
        EndPoint? LocalEndPoint { get; }

        /// <summary>
        /// 连接状态
        /// </summary>
        KcpTransportState State { get; }

        /// <summary>
        /// 异步连接到指定终结点
        /// </summary>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>连接任务</returns>
        Task ConnectAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步发送数据
        /// </summary>
        /// <param name="buffer">数据缓冲区</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送任务</returns>
        ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步接收数据
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>接收到的数据</returns>
        ValueTask<byte[]> ReceiveAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步关闭连接
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>关闭任务</returns>
        Task CloseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 强制断开连接
        /// </summary>
        void ForceClose();
    }

    /// <summary>
    /// 传输层状态
    /// </summary>
    public enum KcpTransportState
    {
        /// <summary>
        /// 未初始化
        /// </summary>
        None = 0,

        /// <summary>
        /// 正在连接
        /// </summary>
        Connecting = 1,

        /// <summary>
        /// 已连接
        /// </summary>
        Connected = 2,

        /// <summary>
        /// 正在关闭
        /// </summary>
        Closing = 3,

        /// <summary>
        /// 已关闭
        /// </summary>
        Closed = 4,

        /// <summary>
        /// 错误状态
        /// </summary>
        Error = 5
    }
}