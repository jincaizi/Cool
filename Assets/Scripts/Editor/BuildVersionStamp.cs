using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Cool.EditorTools
{
    /// <summary>
    /// 出包用的 git 构建标识。只负责读 git；纯函数在 DataDefinition.BuildVersion。
    /// 放在默认 Editor 程序集，与两个出包脚本同程序集，不新增 asmdef。
    /// </summary>
    public static class BuildVersionStamp
    {
        /// <summary>取 git 短 SHA 最后 4 位；失败时 sha4 = "0000" 并返回 false。</summary>
        public static bool TryGetGitShortSha4(out string sha4)
        {
            sha4 = "0000";
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                {
                    UnityEngine.Debug.LogWarning("[BuildVersion] 找不到项目根目录，使用 SHA 0000");
                    return false;
                }

                var startInfo = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    WorkingDirectory = projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return false;

                    if (!process.WaitForExit(3000))
                    {
                        try { process.Kill(); } catch { }
                        UnityEngine.Debug.LogWarning("[BuildVersion] git rev-parse 超时，使用 SHA 0000");
                        return false;
                    }

                    string output = process.StandardOutput.ReadToEnd().Trim();
                    if (process.ExitCode != 0 || output.Length < 4)
                    {
                        UnityEngine.Debug.LogWarning("[BuildVersion] 无法读取 git HEAD，使用 SHA 0000");
                        return false;
                    }

                    sha4 = output.Substring(output.Length - 4);
                    return true;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[BuildVersion] 读取 git SHA 失败，使用 0000：" + e.Message);
                return false;
            }
        }
    }
}
