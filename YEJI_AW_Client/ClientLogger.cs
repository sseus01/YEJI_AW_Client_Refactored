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
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

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
                }

                builder.AppendLine();

                var fileLock = FileLocks.GetOrAdd(filePath, _ => new object());
                lock (fileLock)
                {
                    File.AppendAllText(filePath, builder.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // �α� ���д� �� ���ۿ� ������ ���� �ʴ´�.
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
    }
}
