using System;
using KcpServer;

namespace KcpServer.Tests;

/// <summary>静默 ILogger 实现，供服务端测试复用（SessionManager 等）</summary>
public sealed class TestLogger : ILogger
{
    public bool IsEnabled(LogLevel level) => true;
    public void Log(LogLevel level, Exception? exception, string message, params object[] args) { }
    public void LogDebug(string message, params object[] args) { }
    public void LogInformation(string message, params object[] args) { }
    public void LogWarning(string message, params object[] args) { }
    public void LogError(string message, params object[] args) { }
    public void LogError(Exception ex, string message, params object[] args) { }
    public void LogCritical(string message, params object[] args) { }
    public void LogCritical(Exception ex, string message, params object[] args) { }
}
