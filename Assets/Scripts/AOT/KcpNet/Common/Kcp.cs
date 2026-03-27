// 注意：此实现使用 unsafe 代码，需要在 Unity Player Settings 中启用 "Allow Unsafe Code"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using KCP;

namespace KcpNet
{
    /// <summary>
    /// KCP协议实现（包装Kcp_CSharp_unmanaged库）
    /// 注意：实际项目中应引用Kcp_CSharp_unmanaged NuGet包
    /// </summary>
    public sealed class Kcp : IDisposable
    {
        private readonly KCP.Kcp _nativeKcp;
        private readonly KCP.KcpCallback _nativeOutputCallback;
        private bool _disposed;
        private readonly object _syncRoot = new object();

        /// <summary>
        /// 输出回调（KCP调用此回调发送数据）
        /// </summary>
        public Func<byte[], int, int>? Output { get; set; }

        /// <summary>
        /// 初始化KCP实例
        /// </summary>
        /// <param name="sendWindowSize">发送窗口大小</param>
        /// <param name="receiveWindowSize">接收窗口大小</param>
        public Kcp(uint sendWindowSize, uint receiveWindowSize)
        {
            unsafe
            {
                // 创建委托实例并保存引用，防止被垃圾回收
                _nativeOutputCallback = NativeOutputCallback;
                // 使用默认的会话ID (conv) 和 reserved 参数
                _nativeKcp = new KCP.Kcp(0, _nativeOutputCallback, 0);
                _nativeKcp.SetWindowSize((int)sendWindowSize, (int)receiveWindowSize);
            }
        }

        /// <summary>
        /// 设置KCP参数
        /// </summary>
        /// <param name="nodelay">是否启用无延迟模式</param>
        /// <param name="interval">内部工作间隔（毫秒）</param>
        /// <param name="fastresend">快速重传触发次数</param>
        /// <param name="nocongestioncontrol">是否禁用拥塞控制</param>
        public void NoDelay(int nodelay, int interval, int fastresend, bool nocongestioncontrol)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                _nativeKcp.SetNoDelay(nodelay, interval, fastresend, nocongestioncontrol ? 1 : 0);
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
                _nativeKcp.SetMtu(mtu);
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">数据缓冲区</param>
        /// <param name="offset">偏移量</param>
        /// <param name="length">数据长度</param>
        public void Send(byte[] data, int offset, int length)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset >= data.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || offset + length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));

            lock (_syncRoot)
            {
                unsafe
                {
                    fixed (byte* ptr = &data[offset])
                    {
                        _nativeKcp.Send(ptr, length);
                    }
                }
            }
        }

        /// <summary>
        /// 刷新发送队列
        /// </summary>
        public void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                _nativeKcp.Flush();
            }
        }

        /// <summary>
        /// 输入数据到KCP
        /// </summary>
        /// <param name="data">数据缓冲区</param>
        /// <param name="offset">偏移量</param>
        /// <param name="length">数据长度</param>
        public void Input(byte[] data, int offset, int length)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset >= data.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || offset + length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));

            lock (_syncRoot)
            {
                unsafe
                {
                    fixed (byte* ptr = &data[offset])
                    {
                        _nativeKcp.Input(ptr, length);
                    }
                }
            }
        }

        /// <summary>
        /// 获取下一个可读数据的大小
        /// </summary>
        /// <returns>数据大小（字节）</returns>
        public int PeekSize()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            lock (_syncRoot)
            {
                return _nativeKcp.PeekSize();
            }
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        /// <param name="buffer">接收缓冲区</param>
        /// <param name="offset">偏移量</param>
        /// <param name="length">缓冲区长度</param>
        /// <returns>实际接收的数据长度</returns>
        public int Recv(byte[] buffer, int offset, int length)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || offset + length > buffer.Length) throw new ArgumentOutOfRangeException(nameof(length));

            lock (_syncRoot)
            {
                unsafe
                {
                    fixed (byte* ptr = &buffer[offset])
                    {
                        return _nativeKcp.Receive(ptr, length);
                    }
                }
            }
        }

        /// <summary>
        /// 更新KCP状态
        /// </summary>
        /// <param name="time">当前时间</param>
        public void Update(DateTime time)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Kcp));
            // 将DateTime转换为uint时间戳（毫秒）
            // KCP库的Update接受uint current（毫秒时间戳）
            // 我们使用从Unix Epoch（1970-01-01 UTC）开始的毫秒数，然后取模uint.MaxValue
            // 因为KCP库能处理时间戳环绕
            var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var elapsed = time.ToUniversalTime() - unixEpoch;
            var milliseconds = (uint)(elapsed.TotalMilliseconds % uint.MaxValue);
            lock (_syncRoot)
            {
                _nativeKcp.Update(milliseconds);
            }
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
                _nativeKcp.Dispose();
                Output = null;
            }
        }

        // 本地输出回调，将指针数据转换为字节数组并调用Output委托
        private unsafe void NativeOutputCallback(byte* buffer, int length)
        {
            var output = Output;
            if (output == null) return;

            try
            {
                // 将指针数据复制到字节数组
                byte[] data = new byte[length];
                Marshal.Copy((IntPtr)buffer, data, 0, length);

                // 调用输出回调（返回值被忽略，因为KCP期望void回调）
                output.Invoke(data, length);
            }
            catch
            {
                // 忽略输出回调中的异常，避免传播到原生代码
            }
        }
    }
}