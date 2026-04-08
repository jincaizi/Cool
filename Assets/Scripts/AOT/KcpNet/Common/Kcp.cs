// Kcp 2.7.0 wrapper for Unity client
// Uses System.Net.Sockets.Kcp.Kcp<KcpSegment> API
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace KcpNet
{
    /// <summary>
    /// KCP回调实现 - 实现 IKcpCallback 和 IRentable 接口
    /// </summary>
    internal sealed class KcpCallbackHandler : System.Net.Sockets.Kcp.IKcpCallback, System.Net.Sockets.Kcp.IRentable
    {
        private readonly Socket _socket;
        private readonly EndPoint _remoteEndPoint;
        private readonly ILogger _logger;
        private readonly byte[] _buffer;

        public KcpCallbackHandler(Socket socket, EndPoint remoteEndPoint, ILogger logger, int bufferSize = 65536)
        {
            _socket = socket;
            _remoteEndPoint = remoteEndPoint;
            _logger = logger;
            _buffer = new byte[bufferSize];
        }

        public void Output(IMemoryOwner<byte> buffer, int avalidLength)
        {
            try
            {
                var span = buffer.Memory.Span.Slice(0, avalidLength);
                var data = span.ToArray();
                _socket.SendTo(data, SocketFlags.None, _remoteEndPoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KCP output error");
            }
            finally
            {
                buffer.Dispose();
            }
        }

        public IMemoryOwner<byte> RentBuffer(int needSize)
        {
            if (needSize > _buffer.Length)
            {
                return new SimpleMemoryOwner(new byte[needSize]);
            }
            return new SimpleMemoryOwner(_buffer);
        }

        private sealed class SimpleMemoryOwner : IMemoryOwner<byte>
        {
            private readonly byte[] _array;

            public SimpleMemoryOwner(byte[] array)
            {
                _array = array;
            }

            public Memory<byte> Memory => _array.AsMemory();

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// KCP协议实现（Kcp 2.7.0 包装）
    /// 使用 System.Net.Sockets.Kcp.Kcp<KcpSegment> API
    /// </summary>
    public sealed class Kcp : IDisposable
    {
        private readonly System.Net.Sockets.Kcp.Kcp<System.Net.Sockets.Kcp.KcpSegment> _kcp;
        private readonly KcpCallbackHandler _callbackHandler;
        private readonly ILogger _logger;
        private readonly object _syncRoot = new object();
        private bool _disposed;

        /// <summary>
        /// 初始化KCP实例
        /// </summary>
        /// <param name="conv">会话ID (conv)</param>
        /// <param name="socket">底层socket</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <param name="sendWindowSize">发送窗口大小</param>
        /// <param name="receiveWindowSize">接收窗口大小</param>
        /// <param name="logger">日志记录器</param>
        public Kcp(uint conv, Socket socket, EndPoint remoteEndPoint, int sendWindowSize, int receiveWindowSize, ILogger logger)
        {
            _logger = logger ?? NullLogger.Instance;
            _callbackHandler = new KcpCallbackHandler(socket, remoteEndPoint, _logger);

            // 创建 KCP - 使用 Kcp<Segment> API
            _kcp = new System.Net.Sockets.Kcp.Kcp<System.Net.Sockets.Kcp.KcpSegment>(conv, _callbackHandler, _callbackHandler);
        }

        /// <summary>
        /// 配置KCP参数
        /// </summary>
        /// <param name="nodelay">是否启用无延迟模式</param>
        /// <param name="interval">内部工作间隔（毫秒）</param>
        /// <param name="fastresend">快速重传触发次数</param>
        /// <param name="nocongestioncontrol">是否禁用拥塞控制 (0=启用, 1=禁用)</param>
        public void NoDelay(int nodelay, int interval, int fastresend, int nocongestioncontrol)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                _kcp.NoDelay(nodelay, interval, fastresend, nocongestioncontrol);
            }
        }

        /// <summary>
        /// 设置窗口大小
        /// </summary>
        /// <param name="sendWindow">发送窗口大小</param>
        /// <param name="receiveWindow">接收窗口大小</param>
        public void WndSize(int sendWindow, int receiveWindow)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                _kcp.WndSize(sendWindow, receiveWindow);
            }
        }

        /// <summary>
        /// 设置最大传输单元
        /// </summary>
        /// <param name="mtu">MTU大小</param>
        public void SetMtu(int mtu)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                _kcp.SetMtu(mtu);
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">数据缓冲区</param>
        /// <param name="offset">偏移量</param>
        /// <param name="length">数据长度</param>
        /// <returns>发送结果</returns>
        public int Send(byte[] data, int offset, int length)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                return _kcp.Send(new ReadOnlySpan<byte>(data, offset, length), null);
            }
        }

        /// <summary>
        /// 输入数据到KCP（从网络接收的数据）
        /// </summary>
        /// <param name="data">数据缓冲区</param>
        /// <param name="offset">偏移量</param>
        /// <param name="length">数据长度</param>
        public void Input(byte[] data, int offset, int length)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                _kcp.Input(new ReadOnlySpan<byte>(data, offset, length));
            }
        }

        /// <summary>
        /// 获取下一个可读数据的大小
        /// </summary>
        /// <returns>数据大小（字节），-1表示没有数据</returns>
        public int PeekSize()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                return _kcp.PeekSize();
            }
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        /// <param name="buffer">接收缓冲区</param>
        /// <param name="offset">偏移量</param>
        /// <param name="length">缓冲区长度</param>
        /// <returns>实际接收的数据长度，-1表示没有数据</returns>
        public int Recv(byte[] buffer, int offset, int length)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                return _kcp.Recv(new Span<byte>(buffer, offset, length));
            }
        }

        /// <summary>
        /// 尝试接收数据（推荐使用）
        /// </summary>
        /// <param name="bufferWriter">缓冲区写入器</param>
        /// <returns>接收的字节数，0或负数表示没有数据</returns>
        public int TryRecv(ArrayBufferWriter<byte> bufferWriter)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                return _kcp.TryRecv(bufferWriter);
            }
        }

        /// <summary>
        /// 更新KCP状态
        /// </summary>
        /// <param name="time">当前时间</param>
        public void Update(DateTime time)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                _kcp.Update(DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// 检查KCP是否可释放
        /// </summary>
        public bool CanDispose()
        {
            return _disposed;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            lock (_syncRoot)
            {
                _disposed = true;
                _kcp.Dispose();
            }
        }
    }
}