using System;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    class Program
    {
        private static Server3C? _server;
        private static WebSocketEchoListener? _wsEcho;
        private static WebSocketServerListener? _wsListener;
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

            // --ws-echo：只回吐字节的 WS 端点，用于单独验证 WebGL 的 jslib 通道
            bool wsEcho = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--ws-echo") { wsEcho = true; break; }
            }

            // 2026-10-13 ORCA 避让：--no-avoid 关掉（活体对照的负对照组——与接线前逐字一致）；
            // --probe 打开最小两两间距探针（每 200ms 一行 [Probe] 日志，纯诊断）
            bool noAvoid = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--no-avoid") { noAvoid = true; break; }
            }

            bool probe = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--probe") { probe = true; break; }
            }

            // --ws-prefix：WS 监听前缀。默认本机开发地址——HttpListener 的 host 段是**字面匹配**
            // 请求 Host 头的，而客户端 WebSocketClientAdapter 拼的是 ws://<host>:<port>/、
            // 本项目所有客户端都拨 127.0.0.1：写成 localhost 会得到一个**静默不匹配**
            //（既不转发也不报错，客户端只看到连接被拒）。对外部署的 URL ACL 见 Task 9
            string wsPrefix = "http://127.0.0.1:" + port + "/";
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--ws-prefix" && args[i + 1].Length > 0)
                {
                    wsPrefix = args[i + 1];
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

            if (noAvoid)
            {
                _server.DisableAvoidance();
                Console.WriteLine("Avoidance: disabled (--no-avoid; 智能体按愿望位移直行)");
            }

            if (probe)
            {
                _server.EnableDistanceProbe();
                Console.WriteLine("Probe: min pairwise distance every 200ms");
            }

            try
            {
                await _server.StartAsync(port);

                // D-W3：同进程双栈 —— UDP(KCP) 给原生客户端，TCP(WS) 给浏览器客户端
                //（UDP 8000 与 TCP 8000 不冲突，一个进程即可）。两条来源都汇入同一 ProcessPacket。
                // --ws-echo 是 T4 的纯字节回吐自检端点，与真监听抢同一个 TCP 端口
                //（HttpListener 同一前缀只能注册一个监听器）——两者互斥，显式要求自检时让位给 echo
                if (wsEcho)
                {
                    _wsEcho = new WebSocketEchoListener(logger);
                    _wsEcho.Start(wsPrefix);
                }
                else
                {
                    _wsListener = new WebSocketServerListener(_server, logger, _server.NextSessionId);
                    _wsListener.Start(wsPrefix);
                }

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
                _wsListener?.Dispose();
                _wsEcho?.Dispose();
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
