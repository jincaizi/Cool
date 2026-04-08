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

            Console.WriteLine($"Starting Server3C on port {port}...");

            var logger = new ConsoleLogger();
            _server = new Server3C(logger);

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
