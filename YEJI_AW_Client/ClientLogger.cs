using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace YEJI_AW_Client
{
    public enum ClientLogCategory
    {
        Update,
        Web,
        Service,
        Balloon,
        Agent
    }

    public static class ClientLogger
    {
        private static readonly string LogRoot = Path.Combine("C:\\ProgramData\\YEJI_AW", "logs");
        private static readonly ConcurrentDictionary<string, object> FileLocks = new();

        public static void Log(ClientLogCategory category, string message, string level = "Nml", Exception? exception = null)
        {
            try
            {
                string prefix = GetPrefix(category);
                string filePath = Path.Combine(LogRoot, $"{prefix}_{DateTime.Now:yyyyMMdd}.log");

                // 디렉터리가 없으면 생성
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var builder = new StringBuilder();
                builder.Append('[')
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Append("] ")
                    .Append(level)
                    .Append(" - ")
                    .Append(message);

                if (exception != null)
                {
                    builder.Append(" | ")
                        .Append(exception.GetType().Name)
                        .Append(':')
                        .Append(' ')
                        .Append(exception.Message);

                    // 스택 트레이스가 있는 경우 포함
                    if (!string.IsNullOrEmpty(exception.StackTrace))
                    {
                        builder.AppendLine()
                            .Append("  StackTrace: ")
                            .Append(exception.StackTrace.Replace(Environment.NewLine, Environment.NewLine + "  "));
                    }
                }

                builder.AppendLine();

                var fileLock = FileLocks.GetOrAdd(filePath, _ => new object());
                lock (fileLock)
                {
                    File.AppendAllText(filePath, builder.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                // 로그 기록 실패는 프로그램 동작에 영향을 주지 않도록 무시합니다.
                // 디버그 모드에서는 콘솔에 출력하여 문제를 파악할 수 있도록 합니다.
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ClientLogger] 로그 기록 실패: {ex.Message}");
#endif
            }
        }

        public static void LogUpdate(string message, string level = "Nml", Exception? exception = null) =>
            Log(ClientLogCategory.Update, message, level, exception);

        public static void LogWeb(string message, string level = "Nml", Exception? exception = null) =>
            Log(ClientLogCategory.Web, message, level, exception);

        public static void LogService(string message, string level = "Nml", Exception? exception = null) =>
            Log(ClientLogCategory.Service, message, level, exception);

        public static void LogBalloon(string message, string level = "Nml", Exception? exception = null) =>
            Log(ClientLogCategory.Balloon, message, level, exception);

        public static void LogAgent(string message, string level = "Nml", Exception? exception = null) =>
            Log(ClientLogCategory.Agent, message, level, exception);

        private static string GetPrefix(ClientLogCategory category) => category switch
        {
            ClientLogCategory.Update => "Up",
            ClientLogCategory.Web => "Wb",
            ClientLogCategory.Service => "Sv",
            ClientLogCategory.Balloon => "Bl",
            ClientLogCategory.Agent => "Ag",
            _ => "Ag"
        };

        /// <summary>
        /// 지정된 일수보다 오래된 로그 파일을 삭제합니다.
        /// </summary>
        /// <param name="daysToKeep">보관할 로그 파일 일수 (기본값: 30일)</param>
        public static void CleanupOldLogs(int daysToKeep = 30)
        {
            if (daysToKeep <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(daysToKeep), "보관 일수는 양수여야 합니다.");
            }

            try
            {
                if (!Directory.Exists(LogRoot))
                    return;

                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var logFiles = Directory.GetFiles(LogRoot, "*.log");

                foreach (var logFile in logFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(logFile);
                        if (fileInfo.LastWriteTime < cutoffDate)
                        {
                            File.Delete(logFile);
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[ClientLogger] 오래된 로그 파일 삭제: {fileInfo.Name}");
#endif
                        }
                    }
                    catch (Exception ex)
                    {
                        // 개별 파일 삭제 실패는 무시하고 계속 진행
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[ClientLogger] 파일 삭제 실패: {ex.Message}");
#endif
                    }
                }
            }
            catch (Exception ex)
            {
                // 로그 정리 실패는 무시
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[ClientLogger] 로그 정리 실패: {ex.Message}");
#endif
            }
        }
    }
}
