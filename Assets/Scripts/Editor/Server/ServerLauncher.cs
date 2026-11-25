using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Cool.EditorTools
{
    /// <summary>
    /// 从 Unity 编辑器启动/停止本地 Server3C。
    /// 必须用 dotnet &lt;dll&gt;：本机 dotnet run 的 apphost 会让 HttpListener 抛「句柄无效」。
    /// PID 存入 EditorPrefs，域重载后 Stop 仍能找到已启动的服务器。
    /// </summary>
    public static class ServerLauncher
    {
        private const int Port = 8000;
        private const string DllRelativePath = "Server/bin/Debug/net8.0/Server3C.dll";
        private const string ExtraArgs = "--monsters 15 --map demoday";
        private const string PidKey = "Cool.ServerLauncher.Pid";

        [MenuItem("Tools/Server/Start", false, 0)]
        public static void StartServer()
        {
            Process running = GetRunningProcess();
            if (running != null)
            {
                Debug.Log("[ServerLauncher] 服务器已在运行（PID " + running.Id + "）");
                return;
            }

            if (IsPortOpen())
            {
                Debug.LogWarning("[ServerLauncher] 端口 " + Port + " 已有服务器在监听（可能是命令行启动），请先停止它");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string dllPath = Path.Combine(projectRoot, DllRelativePath);
            if (!File.Exists(dllPath))
            {
                Debug.LogError("[ServerLauncher] 未找到 " + DllRelativePath + "，请先执行 dotnet build Server/Server3C.csproj");
                return;
            }

            var startInfo = new ProcessStartInfo("dotnet")
            {
                Arguments = "\"" + dllPath + "\" " + Port + " " + ExtraArgs,
                WorkingDirectory = projectRoot,
                UseShellExecute = true,
            };
            Process process = Process.Start(startInfo);
            if (process == null)
            {
                Debug.LogError("[ServerLauncher] 启动进程失败");
                return;
            }

            EditorPrefs.SetInt(PidKey, process.Id);
            Debug.Log("[ServerLauncher] 服务器已启动（PID " + process.Id + "，端口 " + Port + "）");
        }

        [MenuItem("Tools/Server/Stop", false, 1)]
        public static void StopServer()
        {
            Process process = GetRunningProcess();
            if (process == null)
            {
                EditorPrefs.DeleteKey(PidKey);
                Debug.Log("[ServerLauncher] 没有由编辑器启动的服务器在运行");
                return;
            }

            process.Kill();
            process.WaitForExit(3000);
            EditorPrefs.DeleteKey(PidKey);
            Debug.Log("[ServerLauncher] 服务器已停止（PID " + process.Id + "）");
        }

        private static bool IsPortOpen()
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    return client.ConnectAsync("127.0.0.1", Port).Wait(200);
                }
            }
            catch (System.AggregateException)
            {
                return false;
            }
        }

        private static Process GetRunningProcess()
        {
            int pid = EditorPrefs.GetInt(PidKey, 0);
            if (pid <= 0) return null;

            try
            {
                Process process = Process.GetProcessById(pid);
                if (!process.HasExited && process.ProcessName == "dotnet") return process;
            }
            catch (System.ArgumentException)
            {
                // PID 已不存在。
            }
            catch (System.InvalidOperationException)
            {
                // 进程在查询期间退出。
            }

            EditorPrefs.DeleteKey(PidKey);
            return null;
        }
    }
}
