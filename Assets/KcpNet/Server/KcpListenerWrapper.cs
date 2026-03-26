using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// KCP监听器包装器
    /// </summary>
    public sealed class KcpListenerWrapper : IDisposable
    {
        private readonly Socket _socket;
        private readonly KcpOptions _options;
        private readonly ILogger _logger;
        private bool _disposed;

        /// <summary>
        /// 初始化KCP监听器
        /// </summary>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        public KcpListenerWrapper(KcpOptions options, ILogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? NullLogger.Instance;

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.SendBufferSize = _options.SendBufferSize;
            _socket.ReceiveBufferSize = _options.ReceiveBufferSize;
        }

        /// <summary>
        /// 本地终结点
        /// </summary>
        public EndPoint? LocalEndPoint => _socket.LocalEndPoint;

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => !_disposed && _socket.IsBound;

        /// <summary>
        /// 开始监听
        /// </summary>
        /// <param name="port">端口</param>
        public void Start(int port)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpListenerWrapper));

            var localEndPoint = new IPEndPoint(_options.BindAddress, port);
            _socket.Bind(localEndPoint);

            _logger.LogInformation($"KCP listener started on {localEndPoint}");
        }

        /// <summary>
        /// 开始监听
        /// </summary>
        /// <param name="localEndPoint">本地终结点</param>
        public void Start(IPEndPoint localEndPoint)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpListenerWrapper));

            _socket.Bind(localEndPoint);

            _logger.LogInformation($"KCP listener started on {localEndPoint}");
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public void Stop()
        {
            if (_disposed) return;

            try
            {
                _socket.Close();
                _logger.LogInformation("KCP listener stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping KCP listener");
            }
        }

        /// <summary>
        /// 接收UDP数据包
        /// </summary>
        /// <param name="buffer">缓冲区</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <returns>接收到的字节数</returns>
        public int Receive(byte[] buffer, ref EndPoint remoteEndPoint)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpListenerWrapper));

            return _socket.ReceiveFrom(buffer, ref remoteEndPoint);
        }

        /// <summary>
        /// 异步接收UDP数据包
        /// </summary>
        /// <param name="buffer">缓冲区</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <returns>接收任务</returns>
        public Task<int> ReceiveAsync(byte[] buffer, EndPoint remoteEndPoint)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpListenerWrapper));

            return Task.Factory.FromAsync(
                (callback, state) => _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteEndPoint, callback, state),
                (asyncResult) => _socket.EndReceiveFrom(asyncResult, ref remoteEndPoint),
                null);
        }

        /// <summary>
        /// 发送UDP数据包
        /// </summary>
        /// <param name="buffer">缓冲区</param>
        /// <param name="offset">偏移</param>
        /// <param name="size">大小</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <returns>发送的字节数</returns>
        public int Send(byte[] buffer, int offset, int size, EndPoint remoteEndPoint)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpListenerWrapper));

            return _socket.SendTo(buffer, offset, size, SocketFlags.None, remoteEndPoint);
        }

        /// <summary>
        /// 异步发送UDP数据包
        /// </summary>
        /// <param name="buffer">缓冲区</param>
        /// <param name="offset">偏移</param>
        /// <param name="size">大小</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <returns>发送任务</returns>
        public Task<int> SendAsync(byte[] buffer, int offset, int size, EndPoint remoteEndPoint)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpListenerWrapper));

            return Task.Factory.FromAsync(
                (callback, state) => _socket.BeginSendTo(buffer, offset, size, SocketFlags.None, remoteEndPoint, callback, state),
                _socket.EndSendTo,
                null);
        }

        /// <summary>
        /// 检查是否有数据可读
        /// </summary>
        /// <param name="timeoutMicroseconds">超时微秒数</param>
        /// <returns>是否有数据可读</returns>
        public bool Poll(int timeoutMicroseconds = 0)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpListenerWrapper));

            return _socket.Poll(timeoutMicroseconds, SelectMode.SelectRead);
        }

        /// <summary>
        /// 获取可用数据量
        /// </summary>
        public int Available => _socket.Available;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Stop();
            _socket.Dispose();

            _logger.LogInformation("KCP listener wrapper disposed");
        }
    }
}