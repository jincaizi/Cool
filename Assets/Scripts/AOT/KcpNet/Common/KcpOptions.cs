using System;
using System.Net;
using System.Security.Cryptography;

namespace KcpNet
{
    /// <summary>
    /// KCP网络框架配置选项
    /// </summary>
    public sealed class KcpOptions
    {
        // ========== KCP 参数 ==========

        /// <summary>
        /// 发送窗口大小（默认32）
        /// </summary>
        public int SendWindowSize { get; set; } = 32;

        /// <summary>
        /// 接收窗口大小（默认32）
        /// </summary>
        public int ReceiveWindowSize { get; set; } = 32;

        /// <summary>
        /// 是否启用无延迟模式（默认true）
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// 内部更新间隔（毫秒，默认10）
        /// </summary>
        public int Interval { get; set; } = 10;

        /// <summary>
        /// 快速重传触发次数（默认2）
        /// </summary>
        public int FastResend { get; set; } = 2;

        /// <summary>
        /// 是否禁用拥塞控制（默认false）
        /// </summary>
        public bool NoCongestionControl { get; set; } = false;

        /// <summary>
        /// 最大传输单元（MTU，默认1400）
        /// </summary>
        public int Mtu { get; set; } = 1400;

        // ========== 网络参数 ==========

        /// <summary>
        /// 本地绑定IP地址（默认Any）
        /// </summary>
        public IPAddress BindAddress { get; set; } = IPAddress.Any;

        /// <summary>
        /// 本地绑定端口（默认0，系统分配）
        /// </summary>
        public int BindPort { get; set; } = 0;

        /// <summary>
        /// 连接超时（秒，默认30）
        /// </summary>
        public int ConnectionTimeout { get; set; } = 30;

        // ========== 心跳与超时 ==========

        /// <summary>
        /// 心跳发送间隔（秒，默认5）
        /// </summary>
        public int HeartbeatInterval { get; set; } = 5;

        /// <summary>
        /// 心跳超时时间（秒，默认30）
        /// </summary>
        public int HeartbeatTimeout { get; set; } = 30;

        /// <summary>
        /// 会话超时时间（秒，默认300）
        /// </summary>
        public int SessionTimeout { get; set; } = 300;

        // ========== 重连参数 ==========

        /// <summary>
        /// 最大重连尝试次数（默认5）
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 5;

        /// <summary>
        /// 基础重连延迟（秒，默认1）
        /// </summary>
        public int BaseReconnectDelay { get; set; } = 1;

        /// <summary>
        /// 最大重连延迟（秒，默认60）
        /// </summary>
        public int MaxReconnectDelay { get; set; } = 60;

        /// <summary>
        /// 是否启用指数退避（默认true）
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        // ========== 限流参数 ==========

        /// <summary>
        /// 单IP最大连接数（默认100）
        /// </summary>
        public int MaxConnectionsPerIp { get; set; } = 100;

        /// <summary>
        /// 每秒最大消息数（默认1000）
        /// </summary>
        public int MaxMessagesPerSecond { get; set; } = 1000;

        /// <summary>
        /// 消息队列最大容量（默认10000）
        /// </summary>
        public int MaxQueueCapacity { get; set; } = 10000;

        // ========== 多线程配置 ==========

        /// <summary>
        /// 工作线程数量（默认Environment.ProcessorCount）
        /// </summary>
        public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// 网络线程优先级（默认Normal）
        /// </summary>
        public System.Threading.ThreadPriority NetworkThreadPriority { get; set; } = System.Threading.ThreadPriority.Normal;

        /// <summary>
        /// 是否使用专用网络线程（默认true）
        /// </summary>
        public bool UseDedicatedNetworkThread { get; set; } = true;

        // ========== 安全配置 ==========

        /// <summary>
        /// 加密类型
        /// </summary>
        public EncryptionType EncryptionType { get; set; } = EncryptionType.None;

        /// <summary>
        /// 加密密钥（Base64编码）
        /// </summary>
        public string EncryptionKey { get; set; } = string.Empty;

        /// <summary>
        /// 握手超时时间（秒，默认10）
        /// </summary>
        public int HandshakeTimeout { get; set; } = 10;

        /// <summary>
        /// 连接令牌有效期（秒，默认3600）
        /// </summary>
        public int ConnectionTokenValidity { get; set; } = 3600;

        // ========== 性能调优 ==========

        /// <summary>
        /// 发送缓冲区大小（字节，默认65536）
        /// </summary>
        public int SendBufferSize { get; set; } = 65536;

        /// <summary>
        /// 接收缓冲区大小（字节，默认65536）
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 65536;

        /// <summary>
        /// 是否启用Naggle算法（默认false）
        /// </summary>
        public bool UseNagleAlgorithm { get; set; } = false;

        /// <summary>
        /// 批处理发送最大延迟（毫秒，默认10）
        /// </summary>
        public int BatchSendMaxDelay { get; set; } = 10;

        /// <summary>
        /// 批处理发送最大大小（字节，默认1400）
        /// </summary>
        public int BatchSendMaxSize { get; set; } = 1400;
    }

    /// <summary>
    /// 加密类型
    /// </summary>
    public enum EncryptionType
    {
        /// <summary>
        /// 不加密
        /// </summary>
        None = 0,

        /// <summary>
        /// XOR简单加密
        /// </summary>
        Xor = 1,

        /// <summary>
        /// AES加密
        /// </summary>
        Aes = 2,

        /// <summary>
        /// 自定义加密
        /// </summary>
        Custom = 255
    }
}