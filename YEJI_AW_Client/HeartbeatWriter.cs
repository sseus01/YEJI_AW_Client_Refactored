using System;
using System.IO;
using Microsoft.Win32;

public class HeartbeatWriter : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly string _heartbeatFilePath;
    private readonly Action<string>? _log; // 로그 콜백 (optional)

    public HeartbeatWriter(string heartbeatFilePath, double intervalMs = 30000, Action<string>? logAction = null)
    {
        _heartbeatFilePath = heartbeatFilePath ?? throw new ArgumentNullException(nameof(heartbeatFilePath));
        _log = logAction;

        // 디렉토리가 존재하는지 확인
        EnsureDirectoryExists();

        _timer = new System.Timers.Timer(intervalMs);
        _timer.Elapsed += (s, e) => WriteHeartbeat();
        _timer.AutoReset = true;

        // 타이머 시작 전에 즉시 초기 heartbeat 작성
        // 파일이 처음부터 존재하고 최신 상태임을 보장하여
        // Watcher가 오래된 파일을 감지하고 프로세스를 재시작하는 것을 방지
        WriteHeartbeat();

        // 주기적 업데이트를 위해 타이머 시작
        _timer.Start();

        // 절전 모드에서 복구 시 즉시 heartbeat 갱신
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        Log($"HeartbeatWriter started, path={_heartbeatFilePath}, intervalMs={intervalMs}");
    }

    private void EnsureDirectoryExists()
    {
        try
        {
            var dir = Path.GetDirectoryName(_heartbeatFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                // Directory.CreateDirectory는 멱등성 - 디렉토리가 이미 존재해도 예외를 발생시키지 않음
                Directory.CreateDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            Log($"Heartbeat directory create failed: {ex}");
            // 예외를 던지지 않고 생성 실패 로그만 남김(상태 진단용)
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            // 절전 모드에서 복구 시 즉시 heartbeat 갱신
            // System.Timers.Timer는 절전 중 멈추므로 즉시 갱신 필요
            Log("Power mode changed to RESUME - writing immediate heartbeat");
            WriteHeartbeat();
        }
    }

    private void WriteHeartbeat()
    {
        try
        {
            // 쓰기 전에 디렉토리가 존재하는지 확인 (삭제되었을 경우를 대비)
            EnsureDirectoryExists();

            // 현재 UTC 시간을 ISO 8601 형식으로 작성
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
            // 이벤트 구독 해제
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;

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