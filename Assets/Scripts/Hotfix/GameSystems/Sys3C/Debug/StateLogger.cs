using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Sys3C.Debug
{
    /// <summary>
    /// 状态日志条目
    /// </summary>
    public struct StateLogEntry
    {
        public float Timestamp;
        public LogLevel Level;
        public string Message;
        public string Category;

        public StateLogEntry(string category, string message, LogLevel level = LogLevel.Info)
        {
            Timestamp = Time.time;
            Category = category;
            Message = message;
            Level = level;
        }

        public override string ToString()
        {
            return $"[{Timestamp:F2}] [{Level}] [{Category}] {Message}";
        }
    }

    /// <summary>
    /// 状态日志记录器
    /// </summary>
    public static class StateLogger
    {
        private static readonly List<StateLogEntry> _logs = new();
        private static readonly int _maxLogs = 500;
        private static StreamWriter _fileWriter;
        private static string _logFilePath;

        /// <summary>
        /// 记录状态变化
        /// </summary>
        public static void LogStateChange(LayerType layer, string from, string to)
        {
            var entry = new StateLogEntry("State", $"{layer}: {from} → {to}", LogLevel.Info);
            AddLog(entry);
        }

        /// <summary>
        /// 记录事件
        /// </summary>
        public static void LogEvent(string eventName, object data = null)
        {
            var message = data != null ? $"{eventName}: {data}" : eventName;
            var entry = new StateLogEntry("Event", message, LogLevel.Debug);
            AddLog(entry);
        }

        /// <summary>
        /// 记录信息
        /// </summary>
        public static void Log(string category, string message, LogLevel level = LogLevel.Info)
        {
            var entry = new StateLogEntry(category, message, level);
            AddLog(entry);
        }

        /// <summary>
        /// 获取最近的日志
        /// </summary>
        public static List<StateLogEntry> GetRecentLogs(int count = 100)
        {
            var start = Mathf.Max(0, _logs.Count - count);
            var result = new List<StateLogEntry>();
            for (int i = start; i < _logs.Count; i++)
            {
                result.Add(_logs[i]);
            }
            return result;
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        public static void Clear()
        {
            _logs.Clear();
            Debug.Log("[StateLogger] Cleared");
        }

        /// <summary>
        /// 导出到文件
        /// </summary>
        public static void DumpToFile(string path)
        {
            try
            {
                using var writer = new StreamWriter(path);
                foreach (var log in _logs)
                {
                    writer.WriteLine(log.ToString());
                }
                Debug.Log($"[StateLogger] Dumped {_logs.Count} entries to {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StateLogger] Failed to dump to file: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始文件记录
        /// </summary>
        public static void StartFileLogging(string directory = null)
        {
            if (directory == null)
            {
                directory = Application.persistentDataPath;
            }

            var fileName = $"state_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            _logFilePath = Path.Combine(directory, fileName);

            _fileWriter = new StreamWriter(_logFilePath);
            _fileWriter.WriteLine("Timestamp,Level,Category,Message");
            Debug.Log($"[StateLogger] Started file logging: {_logFilePath}");
        }

        /// <summary>
        /// 停止文件记录
        /// </summary>
        public static void StopFileLogging()
        {
            _fileWriter?.Close();
            _fileWriter = null;
            Debug.Log("[StateLogger] Stopped file logging");
        }

        private static void AddLog(StateLogEntry entry)
        {
            _logs.Add(entry);

            // 限制日志数量
            while (_logs.Count > _maxLogs)
            {
                _logs.RemoveAt(0);
            }

            // 输出到控制台
            var logMessage = entry.ToString();
            switch (entry.Level)
            {
                case LogLevel.Debug:
                    Debug.Log(logMessage);
                    break;
                case LogLevel.Info:
                    Debug.Log(logMessage);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(logMessage);
                    break;
                case LogLevel.Error:
                    Debug.LogError(logMessage);
                    break;
            }

            // 写入文件
            if (_fileWriter != null)
            {
                _fileWriter.WriteLine($"{entry.Timestamp},{entry.Level},{entry.Category},{entry.Message.Replace(",", ";")}");
            }
        }
    }
}