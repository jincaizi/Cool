using System;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    class Program
    {
        private static Server3C? _server;
        private static readonly ManualResetEventSlim _shutdownEvent = new ManualResetEventSlim(false);

        static async Task Main(string[] args)
        {
            Console.Title = "Server3C - MMO ARPG 3C Sync Server";

            int port = 8000;
            if (args.Length > 0 && int.TryParse(args[0], out var argPort))
            {
                port = argPort;
            }

            // P3 压测模式：--bots N（N 个移动 bot 注册进世界——供 AoiWorldTests 带宽/视野验收）
            int botCount = 0;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--bots" && int.TryParse(args[i + 1], out var n))
                {
                    botCount = n;
                    break;
                }
            }

            // P5 怪物模式：--monsters N（固定布局怪物——供 MonsterWorldTests 验收）
            int monsterCount = 0;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--monsters" && int.TryParse(args[i + 1], out var m))
                {
                    monsterCount = m;
                    break;
                }
            }

            Console.WriteLine($"Starting Server3C on port {port}...");

            var logger = new ConsoleLogger();
            _server = new Server3C(logger);

            // bot 必须先于 StartAsync 注册（EntityStore 主循环独占——R2）
            if (botCount > 0)
            {
                _server.EnableBots(botCount);
                Console.WriteLine($"Bot mode: {botCount} bots registered");
            }

            if (monsterCount > 0)
            {
                _server.EnableMonsters(monsterCount);
                Console.WriteLine($"Monster mode: {monsterCount} monsters spawned");
            }

            try
            {
                await _server.StartAsync(port);

                Console.WriteLine($"Server3C is running on port {port}");
                Console.WriteLine("Press Ctrl+C to stop...");

                Console.CancelKeyPress += OnCancelKeyPress;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                _shutdownEvent.Wait();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Server error");
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                if (_server != null)
                {
                    await _server.StopAsync();
                    _server.Dispose();
                }
            }

            Console.WriteLine("Server3C shutdown complete");
        }

        private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            Console.WriteLine("Shutdown requested...");
            _shutdownEvent.Set();
        }

        private static void OnProcessExit(object? sender, EventArgs e)
        {
            Console.WriteLine("Process exit requested...");
            _shutdownEvent.Set();
        }
    }

    public class ConsoleLogger : ILogger
    {
        public bool IsEnabled(LogLevel level) => true;

        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            var formattedMessage = string.Format(message, args);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {level}: {formattedMessage}");
            if (exception != null)
            {
                Console.WriteLine($"  Exception: {exception.Message}");
            }
        }

        public void LogDebug(string message, params object[] args)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] DEBUG: {string.Format(message, args)}");
        }

        public void LogInformation(string message, params object[] args)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] INFO: {string.Format(message, args)}");
        }

        public void LogWarning(string message, params object[] args)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] WARN: {string.Format(message, args)}");
        }

        public void LogError(string message, params object[] args)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: {string.Format(message, args)}");
        }

        public void LogError(Exception ex, string message, params object[] args)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: {string.Format(message, args)}");
            Console.WriteLine($"  Exception: {ex.Message}");
        }

        public void LogCritical(string message, params object[] args)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CRITICAL: {string.Format(message, args)}");
        }

        public void LogCritical(Exception ex, string message, params object[] args)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CRITICAL: {string.Format(message, args)}");
            Console.WriteLine($"  Exception: {ex.Message}");
        }
    }
}
