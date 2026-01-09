using System;
using System.IO;
using System.Timers;

public class HeartbeatWriter : IDisposable
{
    private readonly Timer _timer;
    private readonly string _heartbeatFilePath;

    /// <summary>
    /// heartbeatFilePath 예: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "YEJI_AW", "heartbeat", "heartbeat.txt")
    /// intervalMs 기본 30초(30000 ms)
    /// </summary>
    public HeartbeatWriter(string heartbeatFilePath, double intervalMs = 30000)
    {
        _heartbeatFilePath = heartbeatFilePath ?? throw new ArgumentNullException(nameof(heartbeatFilePath));
        var dir = Path.GetDirectoryName(_heartbeatFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _timer = new Timer(intervalMs);
        _timer.Elapsed += (s, e) => WriteHeartbeat();
        _timer.AutoReset = true;
        _timer.Start();
    }

    private void WriteHeartbeat()
    {
        try
        {
            // UTC ISO 8601 포맷
            File.WriteAllText(_heartbeatFilePath, DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            // 실패 시 무시하거나 내부 로그 남기기(클라이언트 로그)
        }
    }

    public void Dispose()
    {
        try
        {
            _timer?.Stop();
            _timer?.Dispose();
        }
        catch { }
    }
}