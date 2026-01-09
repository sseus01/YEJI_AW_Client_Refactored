using System;
using System.IO;

public class HeartbeatWriter : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly string _heartbeatFilePath;
    private readonly Action<string>? _log; // 로그 콜백 (optional)

    public HeartbeatWriter(string heartbeatFilePath, double intervalMs = 30000, Action<string>? logAction = null)
    {
        _heartbeatFilePath = heartbeatFilePath ?? throw new ArgumentNullException(nameof(heartbeatFilePath));
        _log = logAction;

        try
        {
            var dir = Path.GetDirectoryName(_heartbeatFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            Log($"Heartbeat directory create failed: {ex}");
            // 예외를 던지지 않고 생성 실패 로그만 남김(상태 진단용)
        }

        _timer = new System.Timers.Timer(intervalMs);
        _timer.Elapsed += (s, e) => WriteHeartbeat();
        _timer.AutoReset = true;
        _timer.Start();

        Log($"HeartbeatWriter started, path={_heartbeatFilePath}, intervalMs={intervalMs}");
    }

    private void WriteHeartbeat()
    {
        try
        {
            File.WriteAllText(_heartbeatFilePath, DateTime.UtcNow.ToString("o"));
        }
        catch (Exception ex)
        {
            Log($"WriteHeartbeat failed: {ex}");
        }
    }

    public void Dispose()
    {
        try
        {
            _timer?.Stop();
            _timer?.Dispose();
            Log("HeartbeatWriter disposed");
        }
        catch (Exception ex)
        {
            Log($"Heartbeat dispose error: {ex}");
        }
    }

    private void Log(string msg)
    {
        try
        {
            // 프로젝트에 ClientLogger가 있으면 사용
            var loggerType = Type.GetType("YEJI_AW_Client.ClientLogger, YEJI_AW_Client");
            if (loggerType != null)
            {
                var mi = loggerType.GetMethod("LogAgent", new[] { typeof(string), typeof(string) });
                mi?.Invoke(null, new object[] { msg, "DBG" });
            }
            else if (_log != null)
            {
                _log(msg);
            }
            else
            {
                // fallback: 간단한 파일에 기록 (App base dir)
                var baseDir = AppContext.BaseDirectory;
                File.AppendAllText(Path.Combine(baseDir, "heartbeat_debug.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
            }
        }
        catch
        {
            // 로깅 실패는 무시
        }
    }
}