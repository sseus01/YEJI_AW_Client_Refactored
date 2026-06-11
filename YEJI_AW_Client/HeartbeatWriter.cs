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

        // 세션 잠금/해제 시 즉시 heartbeat 갱신
        // Watcher가 잠금 중 120초 타임아웃으로 앱을 재시작하는 것을 방지
        SystemEvents.SessionSwitch += OnSessionSwitch;

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
            // System.Timers.Timer는 절전 중 멈추고 일부 시스템에서 자동으로 재시작되지 않을 수 있음
            Log("Power mode changed to RESUME - writing immediate heartbeat and restarting timer");
            WriteHeartbeat();

            // 타이머를 명시적으로 재시작하여 절전 모드 이후에도 주기적 업데이트가 계속되도록 보장
            // 일부 시스템에서 타이머가 자동으로 재시작되지 않는 문제를 해결
            try
            {
                _timer?.Stop();
                _timer?.Start();
                Log("Timer restarted successfully after resume");
            }
            catch (Exception ex)
            {
                Log($"Failed to restart timer after resume: {ex.Message}");
            }
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // 세션 잠금/해제 시 즉시 heartbeat 갱신
        // 세션 잠금 중에도 타이머가 동작하지만, 안전을 위해 즉시 갱신
        // 이를 통해 Watcher가 120초 타임아웃으로 앱을 재시작하는 것을 방지
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            Log("Session locked - writing immediate heartbeat");
            WriteHeartbeat();
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            Log("Session unlocked - writing immediate heartbeat and restarting timer");
            WriteHeartbeat();

            // 세션 잠금 해제 후 타이머를 명시적으로 재시작
            // 일부 환경에서 잠금 중 타이머가 멈출 수 있으므로 재시작으로 확실하게 동작 보장
            try
            {
                _timer?.Stop();
                _timer?.Start();
                Log("Timer restarted successfully after session unlock");
            }
            catch (Exception ex)
            {
                Log($"Failed to restart timer after session unlock: {ex.Message}");
            }
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

    /// <summary>
    /// 즉시 heartbeat를 갱신합니다. 
    /// 모달 다이얼로그 표시 전 등 120초 타임아웃이 우려되는 경우 호출하세요.
    /// </summary>
    public void ForceUpdate()
    {
        Log("Force heartbeat update requested");
        WriteHeartbeat();
    }

    public void Dispose()
    {
        try
        {
            // 이벤트 구독 해제
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

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
            YEJI_AW_Client.ClientLogger.LogAgent(msg, "DBG");
        }
        catch { }
    }
}