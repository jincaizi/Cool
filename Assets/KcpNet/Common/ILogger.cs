using System;

namespace KcpNet
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// 调试信息
        /// </summary>
        Debug = 0,

        /// <summary>
        /// 一般信息
        /// </summary>
        Information = 1,

        /// <summary>
        /// 警告信息
        /// </summary>
        Warning = 2,

        /// <summary>
        /// 错误信息
        /// </summary>
        Error = 3,

        /// <summary>
        /// 严重错误
        /// </summary>
        Critical = 4
    }

    /// <summary>
    /// 日志记录器接口
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// 记录日志
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="exception">异常（可选）</param>
        /// <param name="message">消息模板</param>
        /// <param name="args">消息参数</param>
        void Log(LogLevel level, Exception? exception, string message, params object[] args);

        /// <summary>
        /// 记录调试日志
        /// </summary>
        void LogDebug(string message, params object[] args);

        /// <summary>
        /// 记录信息日志
        /// </summary>
        void LogInformation(string message, params object[] args);

        /// <summary>
        /// 记录警告日志
        /// </summary>
        void LogWarning(string message, params object[] args);

        /// <summary>
        /// 记录错误日志
        /// </summary>
        void LogError(string message, params object[] args);

        /// <summary>
        /// 记录错误日志（带异常）
        /// </summary>
        void LogError(Exception exception, string message, params object[] args);

        /// <summary>
        /// 记录严重错误日志
        /// </summary>
        void LogCritical(string message, params object[] args);

        /// <summary>
        /// 记录严重错误日志（带异常）
        /// </summary>
        void LogCritical(Exception exception, string message, params object[] args);

        /// <summary>
        /// 是否启用指定级别的日志
        /// </summary>
        bool IsEnabled(LogLevel level);
    }

    /// <summary>
    /// 空日志记录器（不记录任何日志）
    /// </summary>
    public sealed class NullLogger : ILogger
    {
        /// <summary>
        /// 空日志记录器实例
        /// </summary>
        public static readonly NullLogger Instance = new NullLogger();

        private NullLogger() { }

        /// <inheritdoc/>
        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            // 不执行任何操作
        }

        /// <inheritdoc/>
        public void LogDebug(string message, params object[] args)
        {
            // 不执行任何操作
        }

        /// <inheritdoc/>
        public void LogInformation(string message, params object[] args)
        {
            // 不执行任何操作
        }

        /// <inheritdoc/>
        public void LogWarning(string message, params object[] args)
        {
            // 不执行任何操作
        }

        /// <inheritdoc/>
        public void LogError(string message, params object[] args)
        {
            // 不执行任何操作
        }

        /// <inheritdoc/>
        public void LogError(Exception exception, string message, params object[] args)
        {
            // 不执行任何操作
        }

        /// <inheritdoc/>
        public void LogCritical(string message, params object[] args)
        {
            // 不执行任何操作
        }

        /// <inheritdoc/>
        public void LogCritical(Exception exception, string message, params object[] args)
        {
            // 不执行任何操作
        }

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel level)
        {
            return false;
        }
    }

    /// <summary>
    /// 控制台日志记录器（用于调试）
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        /// <summary>
        /// 控制台日志记录器实例
        /// </summary>
        public static readonly ConsoleLogger Instance = new ConsoleLogger();

        private readonly LogLevel _minLevel;

        /// <summary>
        /// 初始化控制台日志记录器
        /// </summary>
        /// <param name="minLevel">最小日志级别</param>
        private ConsoleLogger(LogLevel minLevel = LogLevel.Information)
        {
            _minLevel = minLevel;
        }

        /// <inheritdoc/>
        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            if (!IsEnabled(level)) return;

            var formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
            var prefix = GetLevelPrefix(level);
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            Console.WriteLine($"[{timestamp}] [{prefix}] {formattedMessage}");
            if (exception != null)
            {
                Console.WriteLine($"Exception: {exception.GetType().Name}: {exception.Message}");
                Console.WriteLine($"Stack Trace: {exception.StackTrace}");
            }
        }

        /// <inheritdoc/>
        public void LogDebug(string message, params object[] args)
        {
            Log(LogLevel.Debug, null, message, args);
        }

        /// <inheritdoc/>
        public void LogInformation(string message, params object[] args)
        {
            Log(LogLevel.Information, null, message, args);
        }

        /// <inheritdoc/>
        public void LogWarning(string message, params object[] args)
        {
            Log(LogLevel.Warning, null, message, args);
        }

        /// <inheritdoc/>
        public void LogError(string message, params object[] args)
        {
            Log(LogLevel.Error, null, message, args);
        }

        /// <inheritdoc/>
        public void LogError(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Error, exception, message, args);
        }

        /// <inheritdoc/>
        public void LogCritical(string message, params object[] args)
        {
            Log(LogLevel.Critical, null, message, args);
        }

        /// <inheritdoc/>
        public void LogCritical(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Critical, exception, message, args);
        }

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel level)
        {
            return level >= _minLevel;
        }

        private static string GetLevelPrefix(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "UNK"
            };
        }
    }
}