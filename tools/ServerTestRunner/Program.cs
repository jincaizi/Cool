using System;
using System.IO;
using System.Threading;
using Xunit.Runners;

/// <summary>
/// In-process xunit v2 runner（等价于 VSTest testhost 的 xunit 执行路径）。
/// 官方 `dotnet test` 在本机以 Win32Exception(5) 启动 testhost 失败（见 Server/README.md），
/// 这里用 Xunit.Runners.AssemblyRunner 在单进程内发现/执行测试，供 tools/test-server-inprocess.sh 调用。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "Server.Tests.dll");
        string? filter = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--assembly":
                    if (++i >= args.Length) return Usage("--assembly 缺少路径");
                    assemblyPath = args[i];
                    break;
                case "--filter":
                    if (++i >= args.Length) return Usage("--filter 缺少表达式");
                    filter = args[i];
                    break;
                case "-h":
                case "--help":
                    return Usage(null);
                default:
                    // 允许直接传一个 dll 路径（dotnet test 脚本的第一参数惯例）
                    if (args[i].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        assemblyPath = args[i];
                    else
                        return Usage($"未知参数：{args[i]}");
                    break;
            }
        }

        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"Test assembly not found: {assemblyPath}");
            return 2;
        }

        string? substring = ParseFilter(filter);
        int passed = 0, failed = 0, skipped = 0;
        var done = new ManualResetEventSlim(false);
        ExecutionCompleteInfo? summary = null;

        using var runner = AssemblyRunner.WithoutAppDomain(assemblyPath);
        runner.OnTestPassed = _ => passed++;
        runner.OnTestOutput = info =>
        {
            if (Environment.GetEnvironmentVariable("RUNNER_VERBOSE") == "1")
                Console.WriteLine($"OUT {info.TestDisplayName}: {info.Output}");
        };
        runner.OnTestSkipped = info =>
        {
            skipped++;
            Console.WriteLine($"SKIP {info.TestDisplayName}: {info.SkipReason}");
        };
        runner.OnTestFailed = info =>
        {
            failed++;
            Console.WriteLine($"FAIL {info.TestDisplayName}");
            Console.WriteLine($"  {info.ExceptionMessage}");
            if (!string.IsNullOrWhiteSpace(info.ExceptionStackTrace))
                Console.WriteLine(info.ExceptionStackTrace);
            if (!string.IsNullOrWhiteSpace(info.Output))
                Console.WriteLine($"  output: {info.Output}");
        };
        runner.OnErrorMessage = info =>
            Console.Error.WriteLine($"ERROR [{info.MesssageType}] {info.ExceptionMessage} {info.ExceptionStackTrace}");
        runner.OnExecutionComplete = info =>
        {
            summary = info;
            done.Set();
        };

        if (substring != null)
        {
            runner.TestCaseFilter = testCase =>
            {
                string classAndMethod = testCase.TestMethod.TestClass.Class.Name + "." + testCase.TestMethod.Method.Name;
                return classAndMethod.Contains(substring, StringComparison.OrdinalIgnoreCase)
                    || testCase.DisplayName.Contains(substring, StringComparison.OrdinalIgnoreCase);
            };
        }

        // 串行执行：测试里有固定端口/AOI 时序断言，去掉 testhost 的集合并行可避免本机抖动。
        runner.Start(new AssemblyRunnerStartOptions { Parallel = false });

        if (!done.Wait(TimeSpan.FromMinutes(20)))
        {
            Console.Error.WriteLine("Timed out after 20 minutes; cancelling.");
            runner.Cancel();
            return 3;
        }

        Console.WriteLine();
        Console.WriteLine($"TOTAL={summary!.TotalTests} PASSED={passed} FAILED={failed} SKIPPED={skipped} TIME={summary.ExecutionTime:F2}s");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>兼容 dotnet test 的 `FullyQualifiedName~X` / `DisplayName~X`，也接受裸子串。</summary>
    private static string? ParseFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;
        int tilde = filter.IndexOf('~');
        if (tilde >= 0 && tilde + 1 < filter.Length) return filter.Substring(tilde + 1);
        return filter;
    }

    private static int Usage(string? error)
    {
        if (error != null) Console.Error.WriteLine(error);
        Console.Error.WriteLine("Usage: Runner [--assembly <test.dll>] [--filter <FullyQualifiedName~Substring>]");
        return 2;
    }
}
