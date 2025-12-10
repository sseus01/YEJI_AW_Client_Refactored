#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Imaging;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace YEJI_AW_Client
{
    public partial class Form1 : Form
    {
        private ContextMenuStrip? trayMenu;

        // idleTimer 는 Form1.Designer.cs 에 있다고 가정 (디자이너 타이머)
        // private Timer idleTimer;

        private Timer popupTimer;       // 팝업 시간 체크용
        private Timer pcOffTimer;       // PC 종료 알림용
        private Timer shutdownCountdownTimer; // PC 강제 종료 카운트다운
        private Timer configTimer;      // 서버 client_config 5분마다 갱신
        private Timer memoryTrimTimer;  // 주기적으로 워킹셋 정리
        private Timer heartbeatTimer;   // 주기적 상태 전송
        private Timer updateCheckTimer; // 주기적 업데이트 확인

        private TimeSpan workStartTime;
        private TimeSpan workEndTime;
        private TimeSpan lunchStartTime;
        private TimeSpan pcShutdownTime = new TimeSpan(17, 30, 0);
        private TimeSpan lunchEndTime;

        private DateTime lastInputTime;
        private DateTime idleStartTime;
        private bool isIdle = false;
        private bool hasShownPopup = false;
        private bool idleStartedDuringWork = false;

        // 자리비움 기준 시간 (서버에서 가져와 덮어씀, 기본 10분)
        private TimeSpan idleThreshold = TimeSpan.FromMinutes(10);

        private string employeeName;
        private string employeeId;
        private string computerName = Environment.MachineName;
        private string computerIP;

        private readonly string pendingIdleEventsFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "pending_idle_events.json");

        // client_config.json 저장 위치
        private readonly string clientConfigFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "client_config.json");

        // 클라이언트 상태 전송 실패 기록용
        private readonly string clientStatusLogFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "client_status.log");

        private const string ServerBaseUrl = "http://175.106.99.157:3000";

        private static string GetCurrentVersion()
        {
            try
            {
                return Application.ProductVersion;
            }
            catch
            {
                return "0.0.0";
            }
        }

        private bool isPopupShowing = false;
        private HashSet<string> shownPopupTimes = new HashSet<string>();

        private DateTime suspendStartTime = DateTime.MinValue;
        private DateTime sessionLockStartTime = DateTime.MinValue;
        private bool wasInLunchBreak = false;

        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly TimeSpan heartbeatInterval = TimeSpan.FromMinutes(2);
        private bool isSendingHeartbeat = false;
        private readonly TimeSpan popupCacheDuration = TimeSpan.FromMinutes(5);
        private DateTime lastPopupFetchUtc = DateTime.MinValue;
        private List<PopupSchedule> cachedPopupSchedules = new();
        private DateTime popupKeyDate;
        private const int PopupImageMinWidth = 720;
        private const int PopupImageMinHeight = 560;
        private HashSet<string> processedIdleIntervals = new();
        private DateTime idleKeyDate;
        private bool hasShownPcOffAlert = false;
        private DateTime? pcOffAlertTargetTime;
        private readonly TimeSpan employeeOvertimeCheckInterval = TimeSpan.FromSeconds(60);
        private DateTime pcOffKeyDate;
        private DateTime? scheduledShutdownTime;
        private bool? cachedShutdownExempt;
        private DateTime lastShutdownExemptCheckTime;
        private PcOffSettings pcOffSettings = new();
        private int remainingTempDisableCount;
        private DateTime lastPcOffSettingsFetchTime;
        private bool pcOffCountInitializedForDay;
        private Form? pcOffAlertForm;
        private Label? shutdownCountdownLabel;
        private Label? pcOffStatusLabel;
        private bool isTemporaryDisableActive;

        private Timer? managerNotificationTimer;
        private bool isCheckingManagerNotifications;
        private HashSet<string> lastAlertedManagerNotificationIds = new();
        private ToolStripMenuItem? managerNotificationsMenuItem;
        private DateTime lastManagerNotificationAlertTime = DateTime.MinValue;

        private bool isManagerUser;

        private Timer? employeeOvertimeStatusTimer;
        private bool isCheckingEmployeeOvertimeStatus;
        private Dictionary<string, string> lastKnownOvertimeStatuses = new();        


#if DEBUG
        private DateTime? debugBaseDateTime;
        private DateTime debugAnchorDateTime;
#endif

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        // 서버에서 받아오는 팝업 스케줄 데이터
        public class PopupSchedule
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("scheduled_time")]
            public string ScheduledTime { get; set; } = "";

            [JsonPropertyName("image_url")]
            public string ImageUrl { get; set; } = "";

            [JsonPropertyName("message")]
            public string Message { get; set; } = "";

            [JsonPropertyName("created_at")]
            public DateTime CreatedAt { get; set; }
        }

        private DateTime GetCurrentDateTime()
        {
#if DEBUG
            if (debugBaseDateTime.HasValue)
            {
                var elapsed = DateTime.Now - debugAnchorDateTime;
                return debugBaseDateTime.Value.Add(elapsed);
            }
#endif

            return DateTime.Now;
        }

        private DateTime GetCurrentDate()
        {
            return GetCurrentDateTime().Date;
        }

#if DEBUG
        private void SetDebugCurrentTime(DateTime target)
        {
            debugBaseDateTime = target;
            debugAnchorDateTime = DateTime.Now;
        }

        private void ClearDebugCurrentTime()
        {
            debugBaseDateTime = null;
        }
#endif

        public Form1(string employeeName, string employeeId)
        {
            InitializeComponent();

            this.employeeName = employeeName ?? "";
            this.employeeId = employeeId ?? "";
            this.computerIP = GetLocalIPAddress();

            popupKeyDate = GetCurrentDate();
            idleKeyDate = popupKeyDate;
            pcOffKeyDate = popupKeyDate;

#if DEBUG
            // 디버그 시 단축키/트레이 메뉴로 자리비움 사유 창을 바로 열어볼 수 있도록 키 이벤트 설정
            this.KeyPreview = true;
            this.KeyDown += Form1_DebugKeyDown;
#endif

            // 자리비움 감지 타이머 (디자이너에 있는 idleTimer 사용)
            idleTimer.Interval = 1000;
            idleTimer.Tick += IdleTimer_Tick;
            idleTimer.Start();

            lastInputTime = GetLastInputTime();

            // 팝업 스케줄 체크 타이머 (1분 간격)
            popupTimer = new Timer();
            popupTimer.Interval = 60 * 1000;
            popupTimer.Tick += async (s, e) => await CheckAndShowPopupAsync();
            popupTimer.Start();

            // PC 종료 알림 체크 타이머 (1분 간격)
            pcOffTimer = new Timer();
            pcOffTimer.Interval = 60 * 1000;
            pcOffTimer.Tick += async (s, e) => await CheckPcOffAlertAsync();
            pcOffTimer.Start();

            shutdownCountdownTimer = new Timer();
            shutdownCountdownTimer.Interval = 1000;
            shutdownCountdownTimer.Tick += ShutdownCountdownTimer_Tick;

            // 설정(client_config) 갱신 타이머 (5분간격)
            configTimer = new Timer();
            configTimer.Interval = 5 * 60 * 1000; ; // 5분
            configTimer.Tick += async (s, e) => await FetchWorkTimeFromServerAsync();
            configTimer.Start();

            memoryTrimTimer = new Timer();
            memoryTrimTimer.Interval = 5 * 60 * 1000; // 5분마다 워킹셋 트리밍
            memoryTrimTimer.Tick += (s, e) => MemoryOptimizer.TrimWorkingSet();
            memoryTrimTimer.Start();

            heartbeatTimer = new Timer();
            heartbeatTimer.Interval = (int)heartbeatInterval.TotalMilliseconds;
            heartbeatTimer.Tick += async (s, e) => await SendHeartbeatAsync();

            updateCheckTimer = new Timer();
            updateCheckTimer.Interval = (int)TimeSpan.FromHours(1).TotalMilliseconds; // 1시간 간격
            updateCheckTimer.Tick += async (s, e) => await CheckClientUpdateAsync();

            employeeOvertimeStatusTimer = new Timer();
            employeeOvertimeStatusTimer.Interval = (int)employeeOvertimeCheckInterval.TotalMilliseconds;
            employeeOvertimeStatusTimer.Tick += async (s, e) => await CheckEmployeeOvertimeStatusAsync();

            this.Load += async (s, e) =>
            {
                this.Hide();
                this.ShowInTaskbar = false;
                await ResendPendingIdleEventsAsync();
                await FetchWorkTimeFromServerAsync();
                await CheckClientUpdateAsync();
                await RegisterOrUpdateClientAsync();
                await CheckAfterHoursOnStartupAsync();
                heartbeatTimer.Start();
                updateCheckTimer.Start();
                employeeOvertimeStatusTimer.Start();
            };

            InitializeTrayMenu();
            notifyIcon.BalloonTipClicked += async (s, e) => await OnManagerNotificationBalloonClickedAsync();

            // 종료 시 SHUTDOWN 이벤트 전송
            this.FormClosing += Form1_FormClosing;
            // 프로그램 시작 시 BOOT 이벤트 전송
            _ = SendPcEventAsync("BOOT");

            // 전원(절전/복귀) 이벤트 구독
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            // 세션 잠금/해제 이벤트 구독 (Windows+L 등)
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        }

        // 폼 완전 종료 시 전원 이벤트 구독 해제
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            base.OnFormClosed(e);
        }

        // -----------------------------
        // 서버에서 업무시간/점심시간/자리비움 기준 가져오기
        // -----------------------------
        private async Task FetchWorkTimeFromServerAsync()
        {
            try
            {
                var client = HttpClient;
                string url = ServerBaseUrl + "/api/client-config";
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                var workTimeInfo = await JsonSerializer.DeserializeAsync<WorkTimeInfo>(stream, JsonOptions);

                if (workTimeInfo != null)
                {
                    // 1) 서버 값 적용
                    ApplyWorkTimeInfo(workTimeInfo);

                    // 2) 서버 값 로컬 파일에 저장
                    SaveClientConfig(workTimeInfo);
                    return;
                }
            }
            catch
            {
                // 서버 호출 실패 시 아래에서 로컬 파일/기본값으로 처리
            }

            // 3) 서버 실패하면 로컬 파일에서 시도
            var localInfo = LoadClientConfig();
            if (localInfo != null)
            {
                ApplyWorkTimeInfo(localInfo);
            }
            else
            {
                // 4) 그것도 없으면 기본값
                SetDefaultWorkTime();
            }
        }

        // WorkTimeInfo 하나를 받아서 실제 변수들에 적용하는 공통 함수
        private void ApplyWorkTimeInfo(WorkTimeInfo info)
        {
            workStartTime = SafeParseTime(info.WorkStart, new TimeSpan(9, 30, 0));
            workEndTime = SafeParseTime(info.WorkEnd, new TimeSpan(17, 30, 0));
            pcShutdownTime = SafeParseTime(info.PcShutdownTime, workEndTime);
            lunchStartTime = SafeParseTime(info.LunchStart, new TimeSpan(12, 30, 0));
            lunchEndTime = SafeParseTime(info.LunchEnd, new TimeSpan(13, 30, 0));
            idleThreshold = TimeSpan.FromMinutes(info.IdleThresholdMinutes);
        }

        private static TimeSpan SafeParseTime(string time, TimeSpan fallback)
        {
            if (TimeSpan.TryParse(time, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private void SetDefaultWorkTime()
        {
            var defaultInfo = new WorkTimeInfo();
            ApplyWorkTimeInfo(defaultInfo);

            // 로컬에 저장된 설정이 없는 경우에도 기본값(10분 기준)이 파일로 기록되도록 저장
            SaveClientConfig(defaultInfo);
        }

        // 서버에서 받은 설정을 로컬 JSON 파일에 저장
        private void SaveClientConfig(WorkTimeInfo info)
        {
            try
            {
                string? folder = Path.GetDirectoryName(clientConfigFile);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder!);

                string json = JsonSerializer.Serialize(info, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(clientConfigFile, json, Encoding.UTF8);
            }
            catch
            {
                // 저장 실패는 조용히 무시
            }
        }

        // 로컬 JSON 파일에서 마지막 설정을 읽어오기
        private WorkTimeInfo? LoadClientConfig()
        {
            try
            {
                if (!File.Exists(clientConfigFile))
                    return null;

                string json = File.ReadAllText(clientConfigFile, Encoding.UTF8);
                var info = JsonSerializer.Deserialize<WorkTimeInfo>(json, JsonOptions);
                return info;
            }
            catch
            {
                return null;
            }
        }

        // -----------------------------
        // 클라이언트 자동 업데이트 확인
        // -----------------------------
        private async Task CheckClientUpdateAsync()
        {
            try
            {
                string currentVersion = GetCurrentVersion();
                string url = $"{ServerBaseUrl}/api/client-releases/check?currentVersion={Uri.EscapeDataString(currentVersion)}";

                using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                var checkResult = await JsonSerializer.DeserializeAsync<ClientReleaseCheckResponse>(stream, JsonOptions);

                if (checkResult?.Success == true && checkResult.NeedUpdate && checkResult.Latest != null)
                {
                    await PromptAndDownloadUpdateAsync(checkResult.Latest, currentVersion);
                }
            }
            catch
            {
                // 업데이트 확인 실패는 조용히 무시
            }
        }

        private async Task PromptAndDownloadUpdateAsync(ClientReleaseInfo latestRelease, string currentVersion)
        {
            string releaseNotes = string.IsNullOrWhiteSpace(latestRelease.ReleaseNotes)
                ? string.Empty
                : $"\n\n변경 사항:\n{latestRelease.ReleaseNotes}";

            DialogResult dialogResult = MessageBox.Show(
                $"현재 버전: {currentVersion}\n최신 버전: {latestRelease.Version}\n자동 업데이트를 진행하시겠습니까?{releaseNotes}",
                "새 버전 감지",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (dialogResult != DialogResult.Yes)
                return;

            string downloadUrl = latestRelease.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? latestRelease.DownloadUrl
                : ServerBaseUrl + latestRelease.DownloadUrl;

            string targetFileName = string.IsNullOrWhiteSpace(latestRelease.FileName)
                ? $"YEJI_AW_Client_{latestRelease.Version}.exe"
                : latestRelease.FileName;

            string tempFilePath = Path.Combine(Path.GetTempPath(), targetFileName);

            try
            {
                using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream);
            }
            catch
            {
                MessageBox.Show("업데이트 파일 다운로드에 실패했습니다. 네트워크 연결을 확인한 뒤 다시 시도해주세요.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFilePath,
                    UseShellExecute = true
                });

                Application.Exit();
            }
            catch
            {
                MessageBox.Show($"다운로드된 설치 파일 실행에 실패했습니다. 다음 경로에서 직접 실행해주세요:\n{tempFilePath}");
            }
        }

        // -----------------------------
        // 팝업 스케줄 체크 (각 시간 1회)
        // -----------------------------
        private async Task CheckAndShowPopupAsync()
        {
            if (isPopupShowing) return;

            ClearStalePopupKeys();
            ResetPcOffAlertIfNewDay();

            var popups = await FetchPopupSchedulesAsync();
            if (popups == null || popups.Count == 0) return;

            DateTime now = GetCurrentDateTime();

            foreach (var popup in popups)
            {
                if (string.IsNullOrWhiteSpace(popup.ScheduledTime))
                    continue;

                if (!TimeSpan.TryParse(popup.ScheduledTime.Trim(), out var scheduledTimeSpan))
                    continue;

                var currentDate = GetCurrentDate();
                DateTime scheduledDateTime = currentDate.Add(scheduledTimeSpan);

                // 2분 이상 지나버린 팝업은 무시
                if (now - scheduledDateTime > TimeSpan.FromMinutes(1))
                    continue;

                string popupKey = scheduledDateTime.ToString("yyyyMMddHHmmss");
                var diff = now - scheduledDateTime;

                // 예약시간 기준 -1분 ~ +1분 사이 & 아직 안 띄운 경우만
                if (diff >= TimeSpan.FromMinutes(-1) &&
                    diff <= TimeSpan.FromMinutes(1) &&
                    !shownPopupTimes.Contains(popupKey))
                {
                    shownPopupTimes.Add(popupKey);
                    isPopupShowing = true;
                    await ShowPopupAsync(popup);
                    break;
                }
            }
        }

        private async Task CheckAfterHoursOnStartupAsync()
        {
            await TryShowPcOffAlertAsync(triggeredAfterBoot: true);
        }

        private async Task CheckPcOffAlertAsync()
        {
            await TryShowPcOffAlertAsync(triggeredAfterBoot: false);
        }

        private async Task TryShowPcOffAlertAsync(bool triggeredAfterBoot)
        {
            try
            {
                ResetPcOffAlertIfNewDay();

                DateTime now = GetCurrentDateTime();

                await EnsurePcOffSettingsAsync();

                if (await IsShutdownExemptAsync(now))
                    return;

                if (pcOffAlertTargetTime == null)
                {
                    pcOffAlertTargetTime = GetCurrentDate().Add(pcShutdownTime);
                    hasShownPcOffAlert = false;
                }

                if (hasShownPcOffAlert)
                    return;

                if (now >= pcOffAlertTargetTime.Value)
                {
                    hasShownPcOffAlert = true;
                    await ShowPcOffAlertAsync(now, pcOffAlertTargetTime.Value, triggeredAfterBoot, isTemporaryDisableActive);
                    isTemporaryDisableActive = false;
                }
            }
            catch
            {
                // 조용히 무시
            }
        }

        private async Task<bool> IsShutdownExemptAsync(DateTime now)
        {
            if (cachedShutdownExempt.HasValue &&
               (now - lastShutdownExemptCheckTime) < TimeSpan.FromMinutes(1))
            {
                return cachedShutdownExempt.Value;
            }

            bool exempt = await HasApprovedOvertimeTodayAsync(now) || await HasActiveShutdownExceptionAsync(now);
            cachedShutdownExempt = exempt;
            lastShutdownExemptCheckTime = now;
            return exempt;
        }

        private async Task<bool> HasApprovedOvertimeTodayAsync(DateTime now)
        {
            try
            {
                string today = now.ToString("yyyy-MM-dd");
                var url = $"{ServerBaseUrl}/api/overtime-requests?employeeId={Uri.EscapeDataString(employeeId)}&startDate={today}&endDate={today}";
                using var response = await HttpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                foreach (var entry in EnumerateArrayLike(root))
                {
                    string status = GetElementString(entry, "status", "approvalStatus", "approval_status", "result");
                    if (!string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string workDate = GetElementString(entry, "workDate", "work_date", "date");
                    if (DateTime.TryParse(workDate, out var parsedDate) && parsedDate.Date == now.Date)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // 실패 시 예외 사용자로 간주하지 않음
            }

            return false;
        }

        private async Task<bool> HasActiveShutdownExceptionAsync(DateTime now)
        {
            try
            {
                string fromDate = now.AddDays(-1).ToString("yyyy-MM-dd");
                string toDate = now.AddDays(1).ToString("yyyy-MM-dd");
                var url = $"{ServerBaseUrl}/api/shutdown-exceptions?employeeId={Uri.EscapeDataString(employeeId)}&startDate={fromDate}&endDate={toDate}";
                using var response = await HttpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                foreach (var entry in EnumerateArrayLike(root))
                {
                    string targetComputer = GetElementString(entry, "computerName", "computer_name", "pcName", "pc_name", "hostname");
                    if (!string.IsNullOrWhiteSpace(targetComputer) &&
                        !string.Equals(targetComputer, computerName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string workDate = GetElementString(entry, "workDate", "work_date", "date", "targetDate", "target_date");
                    DateTime baseDate = now.Date;
                    if (DateTime.TryParse(workDate, out var parsedDate))
                    {
                        baseDate = parsedDate.Date;
                    }

                    string from = GetElementString(entry, "from_time", "fromTime", "start_time", "startTime", "from");
                    string to = GetElementString(entry, "to_time", "toTime", "end_time", "endTime", "to");

                    if (!TimeSpan.TryParse(from, out var fromTime) || !TimeSpan.TryParse(to, out var toTime))
                    {
                        if (DateTime.TryParse(from, out var startDateTime) && DateTime.TryParse(to, out var endDateTime))
                        {
                            if (now >= startDateTime && now <= endDateTime)
                            {
                                return true;
                            }

                            continue;
                        }

                        return true;
                    }
                                        
                    var start = baseDate.Add(fromTime);
                    var end = baseDate.Add(toTime);
                    if (end < start)
                    {
                        end = end.AddDays(1);
                    }

                    if (now >= start && now <= end)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // 실패 시 예외 사용자로 간주하지 않음
            }

            return false;
        }

        private static IEnumerable<JsonElement> EnumerateArrayLike(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    yield return item;
                }
                yield break;
            }

            foreach (var name in new[] { "data", "items", "content" })
            {
                if (root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        yield return item;
                    }
                    yield break;
                }
            }
        }

        private static string GetElementString(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                {
                    return value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : value.ToString();
                }
            }

            return string.Empty;
        }

        private async Task ShowPcOffAlertAsync(DateTime now, DateTime offTime, bool triggeredAfterBoot, bool isFollowUpAlert)
        {
            ScheduleShutdown(GetCurrentDateTime().AddMinutes(1));

            if (!pcOffCountInitializedForDay)
            {
                remainingTempDisableCount = pcOffSettings.TempDisableCount;
                pcOffCountInitializedForDay = true;
            }

            shutdownCountdownLabel = null;
            pcOffStatusLabel = null;            

            pcOffAlertForm?.Close();
            pcOffAlertForm?.Dispose();

            pcOffAlertForm = new Form
            {
                Text = "PC 종료 알림",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(760, 220),
                TopMost = true,
                ShowInTaskbar = false
            };

            var container = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
            };

            container.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 35));

            var messagePanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                BackColor = Color.White
            };

            string headline = isFollowUpAlert
               ? "연장 시간이 종료되어 PC 종료까지 1분이 남았습니다."
               : $"업무시간이 종료되어 PC가 종료됩니다(기준 시각: {offTime:HH:mm}). 일시해제후 진행 업무는 연장근무에 해당되지 않습니다.";

            var messageLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 64,
                Text = headline,
                Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold)
            };

            shutdownCountdownLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "1분 후 PC가 강제종료됩니다.",
                Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Regular)
            };

            string statusText = triggeredAfterBoot || remainingTempDisableCount <= 0
                ? "1분 후 PC가 강제종료됩니다."
                : $"일시 해제 신청 가능 횟수: {Math.Max(0, remainingTempDisableCount)}회";

            pcOffStatusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = statusText,
                Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Regular)
            };

            messagePanel.Controls.Add(pcOffStatusLabel);
            messagePanel.Controls.Add(shutdownCountdownLabel);
            messagePanel.Controls.Add(messageLabel);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(16, 0, 16, 8)
            };

            var closeButton = new Button
            {
                Text = "확인",
                DialogResult = DialogResult.OK,
                Width = 120,
                Height = 32
            };

            var extendButton = new Button
            {
                Text = "일시해제신청",
                Width = 120,
                Height = 32,
                Visible = remainingTempDisableCount > 0
            };

            extendButton.Click += (s, e) =>
            {
                if (remainingTempDisableCount <= 0)
                {
                    extendButton.Visible = false;
                    pcOffStatusLabel!.Text = "1분 후 PC가 강제종료됩니다.";
                    return;
                }

                remainingTempDisableCount--;
                shutdownCountdownTimer.Stop();
                scheduledShutdownTime = null;
                isTemporaryDisableActive = true;
                pcOffAlertTargetTime = GetCurrentDateTime().AddMinutes(pcOffSettings.TempUsageMinutes);
                hasShownPcOffAlert = false;

                MessageBox.Show(
                    $"{pcOffSettings.TempUsageMinutes}분 후 PC 종료 안내가 다시 표시됩니다. 진행중인 업무를 마무리 해주시기 바랍니다.",
                    "일시 해제 신청 완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                pcOffAlertForm?.Close();               
            };

            closeButton.Click += (s, e) => pcOffAlertForm?.Close();

            buttonPanel.Controls.Add(closeButton);
            buttonPanel.Controls.Add(extendButton);

            container.Controls.Add(messagePanel, 0, 0);
            container.Controls.Add(buttonPanel, 0, 1);
            pcOffAlertForm.Controls.Add(container);
            pcOffAlertForm.AcceptButton = closeButton;
            pcOffAlertForm.CancelButton = closeButton;

            pcOffAlertForm.FormClosed += (s, e) =>
            {
                pcOffAlertForm = null;
                shutdownCountdownLabel = null;
                pcOffStatusLabel = null;
            };

            pcOffAlertForm.Show();
            UpdateShutdownCountdownLabel();
        }

        private void ScheduleShutdown(DateTime targetTime)
        {
            scheduledShutdownTime = targetTime;
            shutdownCountdownTimer.Start();
            UpdateShutdownCountdownLabel();
        }

        private void ShutdownCountdownTimer_Tick(object? sender, EventArgs e)
        {
            if (!scheduledShutdownTime.HasValue)
            {
                shutdownCountdownTimer.Stop();
                return;
            }

            UpdateShutdownCountdownLabel();

            if (GetCurrentDateTime() >= scheduledShutdownTime.Value)
            {
                shutdownCountdownTimer.Stop();
                ForceShutdown();
            }
        }

        private void UpdateShutdownCountdownLabel()
        {
            if (!shutdownCountdownLabel?.IsHandleCreated ?? true)
            {
                return;
            }

            if (!scheduledShutdownTime.HasValue)
            {
                shutdownCountdownLabel!.Text = string.Empty;
                return;
            }

            var remaining = scheduledShutdownTime.Value - GetCurrentDateTime();
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            if (pcOffStatusLabel != null && remaining <= TimeSpan.FromMinutes(1) && remainingTempDisableCount <= 0)
            {
                pcOffStatusLabel.Text = "1분 후 PC가 강제종료됩니다.";
            }

            shutdownCountdownLabel!.Text = $"남은 시간: {(int)remaining.TotalSeconds}초";
        }

        private void ForceShutdown()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/s /f /t 0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch
            {
                // 강제 종료 실행 실패 시 추가 조치는 없음
            }
        }

        // 팝업 표시 (항상 최상단 유지)
        private async Task ShowPopupAsync(PopupSchedule popup)
        {
            var maxImageSize = GetPopupMaxImageSize();

            var popupImage = await LoadScaledImageAsync(ServerBaseUrl + popup.ImageUrl, maxImageSize.Width, maxImageSize.Height);
            if (popupImage == null)
            {
                isPopupShowing = false;
                return;
            }

            var popupForm = new Form
            {
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(popupImage.Width, popupImage.Height),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false
            };

            var pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Hand,
                Image = popupImage
            };
                      
            // 더블클릭으로 닫기
            pictureBox.DoubleClick += (s, e) => popupForm.Close();

            // 포커스를 잃어도 다시 최상단으로 끌어올리기
            popupForm.Deactivate += (s, e) =>
            {
                popupForm.TopMost = true;
                popupForm.BringToFront();
                popupForm.Activate();
            };

            popupForm.FormClosed += (s, e) =>
            {
                if (pictureBox.Image != null)
                {
                    pictureBox.Image.Dispose();
                    pictureBox.Image = null;
                }

                pictureBox.Dispose();
                isPopupShowing = false;
                MemoryOptimizer.TrimWorkingSet();
            };

            popupForm.Controls.Add(pictureBox);
            popupForm.Show();
        }

        private static Size GetScaledSize(Size original, int maxWidth, int maxHeight)
        {
            double widthRatio = (double)maxWidth / original.Width;
            double heightRatio = (double)maxHeight / original.Height;
            double ratio = Math.Min(1.0, Math.Min(widthRatio, heightRatio));

            return new Size((int)(original.Width * ratio), (int)(original.Height * ratio));
        }

        private void ClearStalePopupKeys()
        {
            var currentDate = GetCurrentDate();

            if (popupKeyDate != currentDate)
            {
                shownPopupTimes.Clear();
                popupKeyDate = currentDate;
            }
        }

        private void ClearStaleIdleKeys()
        {
            var currentDate = GetCurrentDate();

            if (idleKeyDate != currentDate)
            {
                processedIdleIntervals.Clear();
                idleKeyDate = currentDate;
            }
        }

        private void ResetPcOffAlertIfNewDay()
        {
            var currentDate = GetCurrentDate();

            if (pcOffKeyDate != currentDate)
            {
                hasShownPcOffAlert = false;
                pcOffAlertTargetTime = null;
                scheduledShutdownTime = null;
                cachedShutdownExempt = null;
                shutdownCountdownTimer.Stop();
                pcOffKeyDate = currentDate;
                pcOffCountInitializedForDay = false;
                remainingTempDisableCount = 0;
                isTemporaryDisableActive = false;
            }
        }

        private async Task EnsurePcOffSettingsAsync()
        {
            var now = GetCurrentDateTime();
            if ((now - lastPcOffSettingsFetchTime) < TimeSpan.FromMinutes(5) && pcOffSettings != null)
            {
                return;
            }

            try
            {
                string url = $"{ServerBaseUrl}/api/pc-off-settings";
                using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    var settings = await JsonSerializer.DeserializeAsync<PcOffSettings>(stream, JsonOptions);
                    if (settings != null)
                    {
                        pcOffSettings = NormalizePcOffSettings(settings);
                        if (!pcOffCountInitializedForDay)
                        {
                            remainingTempDisableCount = pcOffSettings.TempDisableCount;
                        }
                    }
                }
            }
            catch
            {
                // 실패 시 이전 설정 유지
            }
            finally
            {
                lastPcOffSettingsFetchTime = now;
            }
        }

        private PcOffSettings NormalizePcOffSettings(PcOffSettings source)
        {
            return new PcOffSettings
            {
                TempDisableCount = Math.Max(0, source.TempDisableCount),
                TempUsageMinutes = Math.Max(1, source.TempUsageMinutes),
                TempPopupImage = source.TempPopupImage ?? string.Empty
            };
        }

        private string GetIdleIntervalKey(DateTime start, DateTime end)
        {
            return $"{start:yyyyMMddHHmmssfff}-{end:yyyyMMddHHmmssfff}";
        }
               
        private async Task<Image?> LoadScaledImageAsync(string url, int maxWidth, int maxHeight)
        {
            try
            {
                using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var originalImage = Image.FromStream(stream);
                var targetSize = GetScaledSize(originalImage.Size, maxWidth, maxHeight);

                using var resized = new Bitmap(targetSize.Width, targetSize.Height);
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.DrawImage(originalImage, 0, 0, targetSize.Width, targetSize.Height);
                }

                var optimized = new Bitmap(resized.Width, resized.Height, PixelFormat.Format16bppRgb565);
                using (var targetGraphics = Graphics.FromImage(optimized))
                {
                    targetGraphics.DrawImage(resized, 0, 0, resized.Width, resized.Height);
                }

                return optimized;
            }
            catch
            {
                return null;
            }
        }

        private string ResolveImageUrl(string imageUrl)
        {
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
            {
                return imageUrl;
            }

            return $"{ServerBaseUrl.TrimEnd('/')}/{imageUrl.TrimStart('/')}";

        }

        private Size GetPopupMaxImageSize()
        {
            var screenBounds = Screen.PrimaryScreen?.Bounds;
            const int padding = 24; // 화면 가득 차지하지 않도록 최소 여백 확보

            int maxWidth = Math.Max(PopupImageMinWidth, (screenBounds?.Width ?? PopupImageMinWidth) - padding);
            int maxHeight = Math.Max(PopupImageMinHeight, (screenBounds?.Height ?? PopupImageMinHeight) - padding);

            return new Size(maxWidth, maxHeight);
        }

        private async Task<List<PopupSchedule>> FetchPopupSchedulesAsync()
        {
            if (DateTime.UtcNow - lastPopupFetchUtc <= popupCacheDuration && cachedPopupSchedules.Count > 0)
            {
                return cachedPopupSchedules;
            }

            try
            {
                var client = HttpClient;
                string url = ServerBaseUrl + "/api/scheduled-popups";
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                var popups = await JsonSerializer.DeserializeAsync<List<PopupSchedule>>(stream, JsonOptions)
                    ?? new List<PopupSchedule>();
                cachedPopupSchedules = popups;
                lastPopupFetchUtc = DateTime.UtcNow;

                return popups;
            }
            catch
            {
                if (cachedPopupSchedules.Count > 0)
                {
                    return cachedPopupSchedules;
                }

                return new List<PopupSchedule>();
            }
        }

        // -----------------------------
        // 자리비움 감지 (퇴근 이후는 자동 기록)
        // -----------------------------
        private async void IdleTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                DateTime now = GetCurrentDateTime();
                var nowTime = now.TimeOfDay;

                bool isLunchBreak = IsLunchBreak(nowTime);
                if (wasInLunchBreak && !isLunchBreak)
                {
                    lastInputTime = now;
                    isIdle = false;
                    hasShownPopup = false;
                    idleStartedDuringWork = false;
                }
                wasInLunchBreak = isLunchBreak;

                bool isWorkingTime = IsWorkingTime(nowTime);                

                bool isAfterWork = nowTime > workEndTime;

                DateTime currentInputTime = GetLastInputTime();
                bool inputDetected = (currentInputTime - lastInputTime).TotalSeconds > 1;

                // 1) 근무시간: 기존 팝업 방식
                if (!isAfterWork && isWorkingTime)
                {
                    if (inputDetected)
                    {
                        if (isIdle && !hasShownPopup)
                        {
                            hasShownPopup = true;
                            DateTime idleEndTime = currentInputTime;
                            await HandleIdleIntervalAsync(idleStartTime, idleEndTime);                          
                            isIdle = false;
                            idleStartedDuringWork = false;
                        }
                        lastInputTime = currentInputTime;
                    }
                    else
                    {
                        if (!isIdle && (now - lastInputTime) > idleThreshold)
                        {
                            isIdle = true;
                            hasShownPopup = false;
                            idleStartTime = AdjustIdleStartForWorkDay(lastInputTime);
                            idleStartedDuringWork = true;
                        }
                    }

                    return;
                }

                // 2) 근무시간도 아니고 퇴근 후도 아닌 구간(점심 등): 무시
                if (!isAfterWork && !isWorkingTime)
                {
                    lastInputTime = currentInputTime;
                    return;
                }

                // 3) 퇴근 이후: 팝업 없이 자동 기록 (업무종료 / 업무외시간)
                if (isAfterWork)
                {
                    if (inputDetected)
                    {
                        if (isIdle)
                        {
                            DateTime idleEndTime = currentInputTime;
                            if (idleStartedDuringWork)
                            {
                                await HandleIdleIntervalAsync(idleStartTime, idleEndTime);
                                idleStartedDuringWork = false;
                            }
                            else
                            {
                                var afterWorkIdleDuration = idleEndTime - idleStartTime;
                                if (afterWorkIdleDuration >= idleThreshold)
                                {
                                    await SendAfterWorkIdleAsync(idleStartTime, idleEndTime);
                                }
                            }
                            isIdle = false;
                        }
                        lastInputTime = currentInputTime;
                    }
                    else
                    {
                        if (!isIdle && (now - lastInputTime) > idleThreshold && now.TimeOfDay > pcShutdownTime)
                        {
                            isIdle = true;
                            idleStartedDuringWork = false;
                            idleStartTime = lastInputTime;
                        }
                    }
                }
            }
            catch
            {
                // 예외 무시
            }
        }

        private DateTime GetLastInputTime()
        {
            LASTINPUTINFO liI = new LASTINPUTINFO();
            liI.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));

            if (!GetLastInputInfo(ref liI))
                return DateTime.Now;

            uint envTicks = (uint)Environment.TickCount;
            uint lastInputTicks = liI.dwTime;
            uint idleTicks = envTicks >= lastInputTicks
                ? envTicks - lastInputTicks
                : (uint.MaxValue - lastInputTicks) + envTicks;

            return DateTime.Now.AddMilliseconds(-idleTicks);
        }

        private bool IsLunchBreak(TimeSpan time)
        {
            return time >= lunchStartTime && time < lunchEndTime;
        }

        private bool IsWorkingTime(TimeSpan time)
        {
            return time >= workStartTime && time <= workEndTime && !IsLunchBreak(time);
        }

        private DateTime AdjustIdleStartForWorkDay(DateTime start)
        {
            var todayWorkStart = GetCurrentDate().Add(workStartTime);
            if (start < todayWorkStart)
            {
                return todayWorkStart;
            }

            return start;
        }

        // -----------------------------
        // 절전(Sleep) / 복귀 처리
        // -----------------------------
        private async void SystemEvents_PowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                // 절전 들어갈 때 (노트북 덮개 닫음 등)
                suspendStartTime = GetCurrentDateTime();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                // 절전에서 다시 깨어날 때
                DateTime resumeTime = GetCurrentDateTime();

                // suspendStartTime이 유효한 값인지 확인 (프로그램 시작 후 첫 resume 이벤트 방지)
                if (suspendStartTime == DateTime.MinValue)
                {
                    ResetIdleState(resumeTime);
                    return;
                }

                var suspendTimeOfDay = suspendStartTime.TimeOfDay;
                var resumeTimeOfDay = resumeTime.TimeOfDay;

                bool suspendDuringWork = IsWorkingTime(suspendTimeOfDay);
                bool resumeDuringWork = IsWorkingTime(resumeTimeOfDay);

                if (suspendDuringWork || resumeDuringWork)
                {
                    // 절전/복귀 중 하나라도 근무시간이면 자리비움으로 처리
                    // SplitIdleInterval이 근무시간 구간만 추출함
                    await HandleIdleIntervalAsync(suspendStartTime, resumeTime);
                }

                ResetIdleState(resumeTime);
            }
        }

        // -----------------------------
        // 세션 잠금/해제 처리 (Windows+L 등)
        // -----------------------------
        private async void SystemEvents_SessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                // 화면 잠금 시작 (Windows+L 등)
                sessionLockStartTime = GetCurrentDateTime();
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                // 화면 잠금 해제
                DateTime unlockTime = GetCurrentDateTime();

                // sessionLockStartTime이 유효한 값인지 확인 (프로그램 시작 후 첫 unlock 이벤트 방지)
                if (sessionLockStartTime == DateTime.MinValue)
                {
                    ResetIdleState(unlockTime);
                    return;
                }

                var lockTimeOfDay = sessionLockStartTime.TimeOfDay;
                var unlockTimeOfDay = unlockTime.TimeOfDay;

                bool lockDuringWork = IsWorkingTime(lockTimeOfDay);
                bool unlockDuringWork = IsWorkingTime(unlockTimeOfDay);

                if (lockDuringWork || unlockDuringWork)
                {
                    // 잠금/해제 중 하나라도 근무시간이면 자리비움으로 처리
                    // SplitIdleInterval이 근무시간 구간만 추출함
                    await HandleIdleIntervalAsync(sessionLockStartTime, unlockTime);
                }

                ResetIdleState(unlockTime);
            }
        }

        // 자리비움 상태를 리셋하고 마지막 입력 시간을 업데이트
        private void ResetIdleState(DateTime currentTime)
        {
            lastInputTime = currentTime;
            isIdle = false;
            hasShownPopup = false;
        }

        private DateTime GetSystemBootTime()
        {
            try
            {
                // 시스템 업타임(밀리초)을 사용해 부팅 시간을 계산 (추가 참조 불필요)
                return DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private async Task HandleIdleIntervalAsync(DateTime start, DateTime end)
        {
            ClearStaleIdleKeys();
            var segments = SplitIdleInterval(start, end);
            foreach (var segment in segments)
            {
                string intervalKey = GetIdleIntervalKey(segment.Start, segment.End);
                if (processedIdleIntervals.Contains(intervalKey))
                {
                    continue;
                }

                processedIdleIntervals.Add(intervalKey); 
                await ShowIdleReasonPopupAsync(segment.Start, segment.End);
            }
        }

        private List<(DateTime Start, DateTime End)> SplitIdleInterval(DateTime start, DateTime end)
        {
            var segments = new List<(DateTime Start, DateTime End)>();

            if (end <= start)
            {
                return segments;
            }

            DateTime referenceDate = start.Date;
            DateTime workDayStart = referenceDate.Add(workStartTime);
            DateTime lunchStart = referenceDate.Add(lunchStartTime);
            DateTime lunchEnd = referenceDate.Add(lunchEndTime);

            DateTime effectiveStart = start < workDayStart ? workDayStart : start;
            DateTime effectiveEnd = end;

            if (effectiveEnd <= effectiveStart)
            {
                return segments;
            }

            bool overlapsLunch = effectiveStart < lunchEnd && effectiveEnd > lunchStart;

            void TryAddSegment(DateTime segmentStart, DateTime segmentEnd)
            {
                if (segmentEnd <= segmentStart)
                {
                    return;
                }

                TimeSpan duration = segmentEnd - segmentStart;
                if (duration >= idleThreshold)
                {
                    segments.Add((segmentStart, segmentEnd));
                }
            }

            if (!overlapsLunch)
            {
                TryAddSegment(effectiveStart, effectiveEnd);
                return segments;
            }

            if (effectiveStart < lunchStart)
            {
                DateTime beforeLunchEnd = effectiveEnd < lunchStart ? effectiveEnd : lunchStart;
                TryAddSegment(effectiveStart, beforeLunchEnd);
            }

            if (effectiveEnd > lunchEnd)
            {
                DateTime afterLunchStart = effectiveStart > lunchEnd ? effectiveStart : lunchEnd;
                TryAddSegment(afterLunchStart, effectiveEnd);
            }

            return segments;
        }

        private async Task ShowIdleReasonPopupAsync(DateTime start, DateTime end)
        {
            using IdleReasonForm form = new IdleReasonForm(start, end, ServerBaseUrl);
            var result = form.ShowDialog();

            if (result == DialogResult.OK)
            {
                string baseDetail = form.SelectedLevel3 ?? "";
                string detailText = form.DetailReason ?? string.Empty;

                string reasonDetail = string.Empty;
                if (!string.IsNullOrWhiteSpace(detailText))
                {
                    reasonDetail = detailText.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(baseDetail))
                {
                    reasonDetail = baseDetail;
                }
                else if (!string.IsNullOrWhiteSpace(form.SelectedLevel2))
                {
                    reasonDetail = form.SelectedLevel2;
                }
                else if (!string.IsNullOrWhiteSpace(form.SelectedLevel1))
                {
                    reasonDetail = form.SelectedLevel1;
                }
                else
                {
                    reasonDetail = "세부 사유 미입력";
                }

                var idleEvent = new IdleEventData
                {
                    Id = Guid.NewGuid().ToString(),
                    EmployeeId = employeeId,
                    EmployeeName = employeeName,
                    ComputerName = computerName,
                    ComputerIP = computerIP,
                    IdleStartTime = start.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    IdleEndTime = end.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ReasonCategory = form.SelectedLevel1 ?? "",
                    ReasonDetail = reasonDetail,
                    ReasonCode = form.SelectedReasonCode ?? "",
                    ReasonLevel1 = form.SelectedLevel1 ?? "",
                    ReasonLevel2 = form.SelectedLevel2 ?? "",
                    ReasonLevel3 = form.SelectedLevel3 ?? ""
                };

                bool success = await SendIdleEventAsync(idleEvent);
                if (!success)
                {
                    SavePendingIdleEvent(idleEvent);
                    MessageBox.Show("서버 전송에 실패했습니다. 데이터를 로컬에 저장했습니다.");
                }
            }
        }

        // 퇴근 이후 자동 기록용 (업무종료 / 업무외시간)
        private async Task SendAfterWorkIdleAsync(DateTime start, DateTime end)
        {
            var idleEvent = new IdleEventData
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeId = employeeId,
                EmployeeName = employeeName,
                ComputerName = computerName,
                ComputerIP = computerIP,
                IdleStartTime = start.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                IdleEndTime = end.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ReasonCategory = "기타",
                ReasonDetail = "연장근무 이석",
                ReasonCode = "Z99",
                ReasonLevel1 = "기타",
                ReasonLevel2 = "기타",
                ReasonLevel3 = "연장근무 이석"
            };

            await SendIdleEventAsync(idleEvent);
        }
        // -----------------------------
        // 클라이언트 상태 전송 (등록/하트비트)
        // -----------------------------
        private ClientStatusRequest BuildClientStatusPayload()
        {
            computerIP = GetLocalIPAddress();

            return new ClientStatusRequest
            {
                EmpNo = employeeId,
                EmpName = employeeName,
                PcName = computerName,
                ClientVersion = GetCurrentVersion(),
                Ip = computerIP,
                Installed = 1
            };
        }

        private async Task PostClientStatusAsync(string url)
        {
            var payload = BuildClientStatusPayload();
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await HttpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    LogClientStatusIssue(
                        $"[{payload.EmpNo}/{payload.PcName}] {url} 응답 오류",
                        response: response,
                        body: responseBody);
                }
            }
            catch (Exception ex)
            {
                LogClientStatusIssue(
                    $"[{payload.EmpNo}/{payload.PcName}] {url} 요청 예외",
                    ex);
            }
        }

        private void LogClientStatusIssue(string context, Exception? ex = null, HttpResponseMessage? response = null, string? body = null)
        {
            try
            {
                string? folder = Path.GetDirectoryName(clientStatusLogFile);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}");

                if (response != null)
                {
                    sb.AppendLine($"Status: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                if (!string.IsNullOrWhiteSpace(body))
                {
                    sb.AppendLine($"Body: {body}");
                }

                if (ex != null)
                {
                    sb.AppendLine($"Error: {ex}");
                }

                sb.AppendLine();
                File.AppendAllText(clientStatusLogFile, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 로깅 실패는 무시
            }
        }

        private async Task RegisterOrUpdateClientAsync()
        {
            try
            {
                await PostClientStatusAsync($"{ServerBaseUrl}/api/client/register");
                //await PostClientStatusAsync($"{ServerBaseUrl}/api/admin/client-status");
            }
            catch
            {
                // 등록 실패는 조용히 무시 (다음 하트비트에서 재시도)
            }
        }

        private async Task SendHeartbeatAsync()
        {
            if (isSendingHeartbeat)
            {
                return;
            }

            try
            {
                isSendingHeartbeat = true;
                //await PostClientStatusAsync($"{ServerBaseUrl}/api/admin/client-status");
                // /api/client/heartbeat로 변경
                await PostClientStatusAsync($"{ServerBaseUrl}/api/client/heartbeat");
            }
            catch
            {
                // 하트비트 실패 시는 무시 (주기적으로 재시도됨)
            }
            finally
            {
                isSendingHeartbeat = false;
            }
        }


        private async Task<bool> SendIdleEventAsync(IdleEventData data)
        {
            try
            {
                var client = HttpClient;
                string url = $"{ServerBaseUrl}/api/idle-events";
                string json = JsonSerializer.Serialize(data);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    RemovePendingIdleEvent(data.Id);
                    return true;
                }
                else
                {
                    SavePendingIdleEvent(data);
                    return false;
                }
            }
            catch
            {
                SavePendingIdleEvent(data);
                return false;
            }
        }

        // -----------------------------
        // 자리비움 로컬 보관/재전송
        // -----------------------------
        private void SavePendingIdleEvent(IdleEventData data)
        {
            var pendingList = LoadPendingIdleEvents();
            pendingList.Add(data);

            string? folder = Path.GetDirectoryName(pendingIdleEventsFile);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder!);

            File.WriteAllText(pendingIdleEventsFile, JsonSerializer.Serialize(pendingList));
        }

        private List<IdleEventData> LoadPendingIdleEvents()
        {
            if (!File.Exists(pendingIdleEventsFile))
                return new List<IdleEventData>();

            string json = File.ReadAllText(pendingIdleEventsFile);
            return JsonSerializer.Deserialize<List<IdleEventData>>(json) ?? new List<IdleEventData>();
        }

        private void RemovePendingIdleEvent(string id)
        {
            var list = LoadPendingIdleEvents();
            list.RemoveAll(e => e.Id == id);

            string? folder = Path.GetDirectoryName(pendingIdleEventsFile);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder!);

            File.WriteAllText(pendingIdleEventsFile, JsonSerializer.Serialize(list));
        }

        private async Task ResendPendingIdleEventsAsync()
        {
            var pendingList = LoadPendingIdleEvents();
            foreach (var data in new List<IdleEventData>(pendingList))
            {
                bool success = await SendIdleEventAsync(data);
                if (!success)
                {
                    break;
                }
            }
        }

        // -----------------------------
        // PC 이벤트(BOOT / SHUTDOWN)
        // -----------------------------
        private async Task SendPcEventAsync(string eventType)
        {
            try
            {
                var client = HttpClient;
                string url = $"{ServerBaseUrl}/api/pc-events";

                DateTime eventTime = eventType == "BOOT"
                   ? GetSystemBootTime()
                   : GetCurrentDateTime();

                var data = new PcEventData
                {
                    EmployeeId = employeeId,
                    EmployeeName = employeeName,
                    ComputerName = computerName,
                    ComputerIP = computerIP,
                    EventType = eventType,
                    EventTime = eventTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
                };

                string json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                await client.PostAsync(url, content);
            }
            catch
            {
                // 실패해도 별도 저장은 하지 않고 무시
            }
        }

        private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                await SendPcEventAsync("SHUTDOWN");
            }
            catch
            {
                // 종료 중 오류는 무시
            }
        }

        // -----------------------------
        // 트레이 메뉴 & 유틸
        // -----------------------------
        private void InitializeTrayMenu()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("자리비움 이력 보기", null, OnViewIdleHistory);
            trayMenu.Items.Add("사용자 정보 수정", null, OnEditUserInfo);
            trayMenu.Items.Add("연장근무신청", null, OnOpenOvertimeRequest);
            trayMenu.Items.Add("연장근무신청 확인", null, OnOpenOvertimeStatus);

            managerNotificationsMenuItem = new ToolStripMenuItem("연장근무승인 결재", null, async (s, e) => await OpenManagerNotificationsAsync(null))
            {
                Visible = false
            };
            trayMenu.Items.Add(managerNotificationsMenuItem);

#if DEBUG
            trayMenu.Items.Add("디버그: 자리비움 사유 창 열기", null, OnDebugOpenIdleReason);
            trayMenu.Items.Add("디버그: PC오프 알림 확인", null, OnDebugShowPcOffAlert);
            trayMenu.Items.Add("디버그: 연장 근무 신청 창 열기", null, OnDebugOpenOvertimeRequest);
            trayMenu.Items.Add("디버그: 연장 근무 결과 확인", null, OnDebugOpenOvertimeStatus);
            trayMenu.Items.Add("디버그: PC 종료 예외 조회", null, OnDebugOpenShutdownExceptions);
            trayMenu.Items.Add("디버그: 현재 시각 모의 설정", null, OnDebugSetCurrentTime);
            trayMenu.Items.Add("디버그: 현재 시각 모의 해제", null, OnDebugClearCurrentTime);
            trayMenu.Items.Add("디버그: 관리자 진단", null, async (s, e) => await CheckAndAddManagerMenuAsync());
            trayMenu.Items.Add("디버그: 연장근무 관리자 알림 확인", null, async (s, e) => await CheckManagerNotificationsAsync(forceShowPopup: true));
            trayMenu.Items.Add("디버그: 연장근무 직원 알림 확인", null, async (s, e) => await CheckEmployeeOvertimeStatusAsync());
#endif

            notifyIcon.ContextMenuStrip = trayMenu;
            notifyIcon.Visible = true;

            // 관리자 여부 비동기 확인: 관리자라면 관리용 메뉴 추가
            _ = CheckAndAddManagerMenuAsync();
        }

        // 관리자 정보 응답 DTO
        private class ManagerPermission
        {
            public string Catcode { get; set; } = string.Empty;
            public string Catcode2 { get; set; } = string.Empty;
            public string Catcode3 { get; set; } = string.Empty;
        }

        private class ManagerInfoDto
        {
            public string EmployeeId { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
        }

        private class ManagerInfoResponse
        {
            public bool Success { get; set; }
            public ManagerInfoDto? Manager { get; set; }
            public List<ManagerPermission>? Permissions { get; set; }
        }

        private class ManagerNotificationItem
        {
            public string Id { get; set; } = string.Empty;
            public string NotificationStatus { get; set; } = string.Empty;
            public OvertimeRequestSummary OvertimeRequest { get; set; } = new();
        }

        private class OvertimeRequestSummary
        {
            public string Id { get; set; } = string.Empty;
            public string EmployeeId { get; set; } = string.Empty;
            public string EmployeeName { get; set; } = string.Empty;
            public string WorkDate { get; set; } = string.Empty;
            public string StartTime { get; set; } = string.Empty;
            public string EndTime { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }

        // 관리자 여부 확인 후 트레이 메뉴에 관리용 항목 추가
        private async Task CheckAndAddManagerMenuAsync()
        {
            try
            {
                var emp = (employeeId ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(emp))
                {
                    return;
                }

                string url = $"{ServerBaseUrl}/api/client/manager-info?employeeId={Uri.EscapeDataString(emp)}";
                using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                    return;

                await using var stream = await response.Content.ReadAsStreamAsync();
                var mgr = await JsonSerializer.DeserializeAsync<ManagerInfoResponse>(stream, JsonOptions);
                bool isManager = mgr?.Success == true && (mgr.Manager != null || (mgr.Permissions != null && mgr.Permissions.Count > 0));
                isManagerUser = isManager;
                if (isManager)
                {
                    EnableManagerNotificationMenu();
                    StartManagerNotificationPolling();
                }
            }
            catch
            {
                // 실패 시 조용히 무시
            }
        }

        private static bool IsDebugBuild()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }

        private void EnableManagerNotificationMenu()
        {
            if (managerNotificationsMenuItem == null)
                return;

            managerNotificationsMenuItem.Visible = true;
            UpdateManagerNotificationMenuLabel(0);
        }

        private void UpdateManagerNotificationMenuLabel(int newCount)
        {
            if (managerNotificationsMenuItem == null)
                return;

            string baseText = "연장근무승인 결재";
            managerNotificationsMenuItem.Text = newCount > 0
                ? $"{baseText} ({newCount}건 대기)"
                : baseText;

            var targetStyle = newCount > 0 ? FontStyle.Bold : FontStyle.Regular;
            if (managerNotificationsMenuItem.Font.Style != targetStyle)
            {
                managerNotificationsMenuItem.Font = new Font(managerNotificationsMenuItem.Font, targetStyle);
            }
        }

        private void StartManagerNotificationPolling()
        {
            if (managerNotificationTimer != null)
                return;

            managerNotificationTimer = new Timer
            {
                Interval = (int)TimeSpan.FromSeconds(45).TotalMilliseconds
            };
            managerNotificationTimer.Tick += async (s, e) => await CheckManagerNotificationsAsync();
            managerNotificationTimer.Start();

            _ = CheckManagerNotificationsAsync();
        }

        private async Task CheckManagerNotificationsAsync(bool forceShowPopup = false)
        {
            if (isCheckingManagerNotifications)
                return;

            if (!isManagerUser && !forceShowPopup)
                return;

            if (string.IsNullOrWhiteSpace(employeeId))
                return;

            isCheckingManagerNotifications = true;
            try
            {
                var notifications = await FetchManagerNotificationsAsync();
                int newCount = notifications.Count(n => string.Equals(n.NotificationStatus, "NEW", StringComparison.OrdinalIgnoreCase));
                UpdateManagerNotificationMenuLabel(newCount);

                var newIds = notifications
                    .Where(n => string.Equals(n.NotificationStatus, "NEW", StringComparison.OrdinalIgnoreCase))
                    .Select(n => n.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet();

                if (newIds.Count == 0)
                {
                    lastAlertedManagerNotificationIds.Clear();
                    lastManagerNotificationAlertTime = DateTime.MinValue;
                    return;
                }

                // 10분마다 알림을 반복적으로 표시 (확인하지 않은 경우)
                bool isNewSet = !newIds.SetEquals(lastAlertedManagerNotificationIds);
                bool shouldShowReminder = lastManagerNotificationAlertTime != DateTime.MinValue
                    && (DateTime.Now - lastManagerNotificationAlertTime) >= TimeSpan.FromMinutes(10);

                if (forceShowPopup || isNewSet || shouldShowReminder)
                {
                    lastAlertedManagerNotificationIds = newIds;
                    lastManagerNotificationAlertTime = DateTime.Now;
                    ShowManagerNotificationAlert(notifications.Where(n => newIds.Contains(n.Id)).ToList());
                }
            }
            catch
            {
                // 조용히 무시 (폴링 실패 시 재시도)
            }
            finally
            {
                isCheckingManagerNotifications = false;
            }
        }

        private async Task<List<ManagerNotificationItem>> FetchManagerNotificationsAsync()
        {
            var list = new List<ManagerNotificationItem>();

            try
            {
                string url = $"{ServerBaseUrl}/api/client/manager-notifications?employeeId={Uri.EscapeDataString(employeeId)}";
                using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    return list;
                }

                string json = await response.Content.ReadAsStringAsync();
                list = ParseManagerNotifications(json);
            }
            catch
            {
                // 실패 시 빈 리스트 반환
            }

            return list;
        }

        private List<ManagerNotificationItem> ParseManagerNotifications(string json)
        {
            var list = new List<ManagerNotificationItem>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var source = root;
                if (root.TryGetProperty("notifications", out var arr))
                {
                    source = arr;
                }

                foreach (var item in EnumerateArrayLike(source))
                {
                    var overtime = item;
                    if (item.TryGetProperty("overtimeRequest", out var o))
                    {
                        overtime = o;
                    }
                    else if (item.TryGetProperty("overtime_request", out var o2))
                    {
                        overtime = o2;
                    }

                    list.Add(new ManagerNotificationItem
                    {
                        Id = GetElementString(item, "id", "_id", "notificationId"),
                        NotificationStatus = GetElementString(item, "notificationStatus", "status"),
                        OvertimeRequest = new OvertimeRequestSummary
                        {
                            Id = GetElementString(overtime, "id", "_id", "requestId", "request_id"),
                            EmployeeId = GetElementString(overtime, "employeeId", "employee_id", "empNo", "emp_no"),
                            EmployeeName = GetElementString(overtime, "employeeName", "employee_name", "empName", "emp_name"),
                            WorkDate = GetElementString(overtime, "workDate", "work_date", "date"),
                            StartTime = GetElementString(overtime, "startTime", "start_time", "start"),
                            EndTime = GetElementString(overtime, "endTime", "end_time", "end"),
                            Reason = GetElementString(overtime, "reason", "description", "comment"),
                            Status = GetElementString(overtime, "status", "approvalStatus", "approval_status", "result"),
                        }
                    });
                }
            }
            catch
            {
                // 파싱 실패 시 빈 리스트 반환
            }

            return list;
        }

        private void ShowManagerNotificationAlert(List<ManagerNotificationItem> newNotifications)
        {
            if (newNotifications.Count == 0)
                return;

            notifyIcon.BalloonTipTitle = "연장근무승인 결재";
            notifyIcon.BalloonTipText = $"새 연장 근무 신청 {newNotifications.Count}건이 도착했습니다. 클릭하여 확인하세요.";
            notifyIcon.ShowBalloonTip(4000);
        }

        private async Task OnManagerNotificationBalloonClickedAsync()
        {
            if (lastAlertedManagerNotificationIds.Count == 0)
            {
                await OpenManagerNotificationsAsync(null);
                return;
            }

            await OpenManagerNotificationsAsync(lastAlertedManagerNotificationIds);
        }

        private async Task OpenManagerNotificationsAsync(IEnumerable<string>? notificationIdsToMark)
        {
            if (!isManagerUser && !IsDebugBuild())
            {
                MessageBox.Show("관리자 전용 기능입니다.");
                return;
            }

            try
            {
                // 알림 목록을 열 때 알림 타이머 리셋 (확인한 것으로 간주)
                lastManagerNotificationAlertTime = DateTime.MinValue;

                var form = new ManagerNotificationListForm(ServerBaseUrl, HttpClient, employeeId, employeeName, notificationIdsToMark);
                ShowTrayMenuForm(form);
                form.Dispose(); // ShowDialog() is synchronous, so this happens after the form closes
                await CheckManagerNotificationsAsync();
            }
            catch (Exception ex)
            {
#if DEBUG
                MessageBox.Show($"연장 근무 알림을 여는 중 오류가 발생했습니다.\n{ex.Message}");
#endif
            }
        }

        // 직원의 연장근무 신청 상태 확인 (승인/반려 시 알림)
        private async Task CheckEmployeeOvertimeStatusAsync()
        {
            if (isCheckingEmployeeOvertimeStatus)
                return;

            if (string.IsNullOrWhiteSpace(employeeId))
                return;

            isCheckingEmployeeOvertimeStatus = true;
            try
            {
                var overtimeRequests = await FetchEmployeeOvertimeRequestsAsync();

                foreach (var request in overtimeRequests)
                {
                    if (string.IsNullOrWhiteSpace(request.Id))
                        continue;

                    string currentStatus = request.Status?.Trim().ToUpperInvariant() ?? string.Empty;

                    // 이전에 알려진 상태가 있는지 확인
                    if (lastKnownOvertimeStatuses.TryGetValue(request.Id, out var previousStatus))
                    {
                        // 상태가 변경되었고, 승인 또는 반려 상태로 변경된 경우에만 알림
                        if (!string.Equals(previousStatus, currentStatus, StringComparison.OrdinalIgnoreCase))
                        {
                            if (currentStatus == "APPROVED" || currentStatus == "REJECTED")
                            {
                                ShowEmployeeOvertimeStatusNotification(request);
                            }
                            // 상태 업데이트 (PENDING으로 돌아가는 경우도 처리)
                            lastKnownOvertimeStatuses[request.Id] = currentStatus;
                        }
                    }
                    else
                    {
                        // 처음 본 요청인 경우, 상태만 기록하고 알림은 표시하지 않음
                        // (프로그램 시작 시 이미 처리된 요청에 대해 알림을 피하기 위함)
                        lastKnownOvertimeStatuses[request.Id] = currentStatus;
                    }
                }
            }
            catch
            {
                // 조용히 무시 (폴링 실패 시 재시도)
            }
            finally
            {
                isCheckingEmployeeOvertimeStatus = false;
            }
        }

        private async Task<List<EmployeeOvertimeRequest>> FetchEmployeeOvertimeRequestsAsync()
        {
            var list = new List<EmployeeOvertimeRequest>();

            try
            {
                // 최근 7일간의 연장근무 신청 조회
                var today = GetCurrentDateTime();
                var startDate = today.AddDays(-7).ToString("yyyy-MM-dd");
                var endDate = today.ToString("yyyy-MM-dd");

                string url = $"{ServerBaseUrl}/api/overtime-requests?employeeId={Uri.EscapeDataString(employeeId)}&startDate={startDate}&endDate={endDate}";
                using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    return list;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                foreach (var item in EnumerateArrayLike(root))
                {
                    var request = new EmployeeOvertimeRequest
                    {
                        Id = GetElementString(item, "id", "requestId", "request_id"),
                        WorkDate = GetElementString(item, "workDate", "work_date", "date"),
                        StartTime = GetElementString(item, "startTime", "start_time", "start"),
                        EndTime = GetElementString(item, "endTime", "end_time", "end"),
                        Reason = GetElementString(item, "reason", "description", "comment"),
                        Status = GetElementString(item, "status", "approvalStatus", "approval_status", "result"),
                        Approver = GetElementString(item, "approver", "approverName", "approvedBy", "approved_by", "approver_name")
                    };

                    if (!string.IsNullOrWhiteSpace(request.Id))
                    {
                        list.Add(request);
                    }
                }
            }
            catch
            {
                // 실패 시 빈 리스트 반환
            }

            return list;
        }

        private void ShowEmployeeOvertimeStatusNotification(EmployeeOvertimeRequest request)
        {
            if (notifyIcon == null)
                return;

            string statusText = request.Status?.Trim().ToUpperInvariant() == "APPROVED" ? "승인" : "반려";
            string approverInfo = !string.IsNullOrWhiteSpace(request.Approver) ? $" (승인자: {request.Approver})" : string.Empty;

            // 시간을 한국 시간(KST)으로 표시
            string startTimeKst = FormatTimeToKst(request.StartTime);
            string endTimeKst = FormatTimeToKst(request.EndTime);

            // WorkDate도 한국 시간으로 표시
            string workDateKst = FormatDateToKst(request.WorkDate);

            notifyIcon.BalloonTipTitle = $"[{statusText}] 연장근무신청";
            notifyIcon.BalloonTipText = $"{workDateKst} {startTimeKst}~{endTimeKst}\n{request.Reason}{approverInfo}";
            notifyIcon.ShowBalloonTip(5000);
        }

        private static DateTime ConvertToUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc)
            {
                return dt;
            }
            else if (dt.Kind == DateTimeKind.Local)
            {
                return dt.ToUniversalTime();
            }
            else
            {
                // Unspecified인 경우 UTC로 간주 (서버에서 UTC로 저장된 것으로 가정)
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
        }

        private static string FormatTimeToKst(string? timeString)
        {
            if (string.IsNullOrWhiteSpace(timeString))
                return string.Empty;

            // 이미 HH:mm 형식인 경우 그대로 반환 (서버에서 KST로 저장된 경우)
            if (TimeSpan.TryParse(timeString, out _))
            {
                return timeString;
            }

            // ISO 8601 datetime 형식인 경우 KST로 변환
            if (DateTime.TryParse(timeString, out var dt))
            {
                try
                {
                    var koreaTz = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
                    var utcTime = ConvertToUtc(dt);
                    var kstTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, koreaTz);
                    return kstTime.ToString("HH:mm");
                }
                catch (TimeZoneNotFoundException)
                {
                    // Fallback: UTC+9 수동 변환
                    var utcTime = ConvertToUtc(dt);
                    var kstTime = utcTime.AddHours(9);
                    return kstTime.ToString("HH:mm");
                }
            }

            return timeString;
        }

        private static string FormatDateToKst(string? dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
                return string.Empty;

            // ISO 8601 datetime 형식인 경우 (예: 2025-12-08T15:00:00Z)
            // 이것이 UTC 시간이므로 KST로 변환하면 날짜가 바뀔 수 있음
            if (DateTime.TryParse(dateString, out var dt))
            {
                // 'Z' 또는 ISO 8601 형식인지 확인
                if (dateString.Contains('T') || dateString.EndsWith('Z'))
                {
                    try
                    {
                        var koreaTz = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
                        var utcTime = ConvertToUtc(dt);
                        var kstTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, koreaTz);
                        return kstTime.ToString("yyyy-MM-dd");
                    }
                    catch (TimeZoneNotFoundException)
                    {
                        // Fallback: UTC+9 수동 변환
                        var utcTime = ConvertToUtc(dt);
                        var kstTime = utcTime.AddHours(9);
                        return kstTime.ToString("yyyy-MM-dd");
                    }
                }

                // 단순 날짜 형식 (yyyy-MM-dd)인 경우 그대로 반환
                return dt.ToString("yyyy-MM-dd");
            }

            return dateString;
        }

        private class EmployeeOvertimeRequest
        {
            public string Id { get; set; } = string.Empty;
            public string WorkDate { get; set; } = string.Empty;
            public string StartTime { get; set; } = string.Empty;
            public string EndTime { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string Approver { get; set; } = string.Empty;
        }



#if DEBUG
        // 트레이에서 관리자 진단을 즉시 실행할 수 있는 디버그 메뉴 추가
        private void AddDebugManagerDiagnosticsMenu()
        {
            if (trayMenu == null) return;
            foreach (ToolStripItem item in trayMenu.Items)
            {
                if (item is ToolStripMenuItem mi && mi.Text.Contains("디버그: 관리자 진단"))
                    return;
            }
            trayMenu.Items.Insert(0, new ToolStripMenuItem("디버그: 관리자 진단", null, async (s, e) => await CheckAndAddManagerMenuAsync()));
        }
#endif

        private async void OnViewManagedIdleHistory(object? sender, EventArgs e)
        {
            try
            {
                var now = GetCurrentDateTime();
                string endDate = now.ToString("yyyy-MM-dd");
                string startDate = now.AddDays(-7).ToString("yyyy-MM-dd");
                string url = $"{ServerBaseUrl}/api/client/manager-logs?employeeId={Uri.EscapeDataString(employeeId)}&startDate={startDate}&endDate={endDate}";
                string json = await HttpClient.GetStringAsync(url);
                var events = ParseIdleEventsFromJson(json);
                var form = new IdleHistoryForm(events);
                ShowTrayMenuForm(form);
                form.Dispose(); // ShowDialog() is synchronous, so this happens after the form closes
            }
            catch
            {
                MessageBox.Show("관리 조직의 자리비움 이력을 가져오는데 실패했습니다.");
            }
        }        

        private void ShowTrayMenuForm(Form form)
        {
            ShowTrayMenuFormInternal(form);
        }

        private DialogResult ShowTrayMenuFormWithResult(Form form)
        {
            return ShowTrayMenuFormInternal(form);
        }

        /// <summary>
        /// 트레이 메뉴 폼을 모달 다이얼로그로 표시합니다.
        /// </summary>
        private DialogResult ShowTrayMenuFormInternal(Form form)
        {
            // 요청한 창을 최상단에 배치하여 뒤로 가려지는 문제를 방지합니다.
            form.TopMost = true;
            form.BringToFront();

            return form.ShowDialog(); // 닫힐 때까지 블록됨
        }

        private async void OnViewIdleHistory(object? sender, EventArgs e)
        {
            // 일반 사용자: 본인 이력, 기본 당일
            var history = await FetchIdleHistoryForDateRangeAsync(employeeId, GetCurrentDate(), GetCurrentDate());

            // 관리자인 경우 조직 이력 보기 버튼을 추가하여 조직 전체 이력을 볼 수 있게 함
            Action? onViewOrgHistory = null;
            if (isManagerUser)
            {
                onViewOrgHistory = () =>
                {
                    var managedForm = new ManagedIdleHistoryForm(ServerBaseUrl, HttpClient, employeeId);
                    ShowTrayMenuForm(managedForm);
                    managedForm.Dispose();
                };
            }

            var form = new IdleHistoryForm(history, isManagerUser, onViewOrgHistory);
            ShowTrayMenuForm(form);
            form.Dispose(); // ShowDialog() is synchronous, so this happens after the form closes
        }

        private async Task<List<IdleEventData>> FetchIdleHistoryForDateRangeAsync(string targetEmpId, DateTime fromDate, DateTime toDate)
        {
            try
            {
                var client = HttpClient;
                string url = $"{ServerBaseUrl}/api/idle-events?employeeId={Uri.EscapeDataString(targetEmpId)}&startDate={fromDate:yyyy-MM-dd}&endDate={toDate:yyyy-MM-dd}";
                string json = await client.GetStringAsync(url);
                var history = JsonSerializer.Deserialize<List<IdleEventData>>(json, JsonOptions);
                return history ?? new List<IdleEventData>();
            }
            catch
            {
                return new List<IdleEventData>();
            }
        }

        private void OnEditUserInfo(object? sender, EventArgs e)
        {
            var userInfoForm = new UserInfoForm();
            userInfoForm.SetUserInfo(employeeName, employeeId);
            var result = ShowTrayMenuFormWithResult(userInfoForm);

            if (result == DialogResult.OK)
            {
                employeeName = userInfoForm.EmployeeName;
                employeeId = userInfoForm.EmployeeId;
                try
                {
                    string folder = @"C:\ProgramData\YEJI_AW";
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    string filePath = Path.Combine(folder, "user_info.json");
                    File.WriteAllText(filePath, JsonSerializer.Serialize(new UserInfo { Name = employeeName, Id = employeeId }));
                }
                catch { }
                MessageBox.Show("사용자 정보가 업데이트 되었습니다.");
            }
            userInfoForm.Dispose();
        }

        private void OnOpenOvertimeRequest(object? sender, EventArgs e)
        {
            var now = GetCurrentDateTime();
            var cutoffTime = new TimeSpan(17, 30, 0);
            if (now.TimeOfDay >= cutoffTime)
            {
                MessageBox.Show("연장 근무 신청은 업무시간(17:30 이전에만 가능합니다.");
                return;
            }
            var form = new OvertimeRequestForm(ServerBaseUrl, HttpClient, employeeId, GetCurrentDateTime);
            ShowTrayMenuForm(form);
            form.Dispose(); // ShowDialog() is synchronous, so this happens after the form closes
        }

        private void OnOpenOvertimeStatus(object? sender, EventArgs e)
        {
            var form = new OvertimeRequestListForm(ServerBaseUrl, HttpClient, employeeId);
            ShowTrayMenuForm(form);
            form.Dispose(); // ShowDialog() is synchronous, so this happens after the form closes
        }

        // ===== 보강: 누락된 핸들러/메서드 구현 (기존 호출 시그니처 유지) =====

        private async void OnDebugOpenIdleReason(object? sender, EventArgs e)
        {
#if DEBUG
            var now = GetCurrentDateTime();
            await ShowIdleReasonPopupAsync(now.AddMinutes(-5), now);
#endif
        }

        private void OnDebugShowPcOffAlert(object? sender, EventArgs e)
        {
#if DEBUG
            // 실제 알림창을 테스트로 띄우도록 변경
            var now = GetCurrentDateTime();
            var offTime = GetCurrentDate().Add(pcShutdownTime);
            _ = ShowPcOffAlertAsync(now, offTime, triggeredAfterBoot: false, isFollowUpAlert: false);
#endif
        }

        private void OnDebugOpenOvertimeRequest(object? sender, EventArgs e)
        {
#if DEBUG
            OnOpenOvertimeRequest(sender, e);
#endif
        }

        private void OnDebugOpenOvertimeStatus(object? sender, EventArgs e)
        {
#if DEBUG
            OnOpenOvertimeStatus(sender, e);
#endif
        }

        private void OnDebugOpenShutdownExceptions(object? sender, EventArgs e)
        {
#if DEBUG
            var form = new ShutdownExceptionListForm(ServerBaseUrl, HttpClient, employeeId);
            ShowTrayMenuForm(form);
            form.Dispose(); // ShowDialog() is synchronous, so this happens after the form closes
#endif
        }

        private void OnDebugSetCurrentTime(object? sender, EventArgs e)
        {
#if DEBUG
            // 간단히 현재 시각을 입력받아 설정
            var selected = PromptForDebugTime();
            if (selected.HasValue)
            {
                SetDebugCurrentTime(selected.Value);
                MessageBox.Show($"모의 시각이 {selected.Value:yyyy-MM-dd HH:mm}으로 설정되었습니다.");
            }
#endif
        }

        private void OnDebugClearCurrentTime(object? sender, EventArgs e)
        {
#if DEBUG
            ClearDebugCurrentTime();
            MessageBox.Show("모의 시각 설정이 해제되었습니다.");
#endif
        }

        private async void Form1_DebugKeyDown(object? sender, KeyEventArgs e)
        {
#if DEBUG
            if (e.Control && e.Shift && e.KeyCode == Keys.R)
            {
                e.Handled = true;
                var now = GetCurrentDateTime();
                await ShowIdleReasonPopupAsync(now.AddMinutes(-5), now);
            }
#endif
        }

        private async Task<List<IdleEventData>> FetchIdleHistoryFromServerAsync()
        {
            try
            {
                var client = HttpClient;
                string url = $"{ServerBaseUrl}/api/idle-events?employeeId={employeeId}";
                string json = await client.GetStringAsync(url);
                var history = JsonSerializer.Deserialize<List<IdleEventData>>(json, JsonOptions);
                return history ?? new List<IdleEventData>();
            }
            catch
            {
#if DEBUG
                MessageBox.Show("자리비움 이력을 가져오는데 실패했습니다.");
#endif
                return new List<IdleEventData>();
            }
        }

        private List<IdleEventData> ParseIdleEventsFromJson(string json)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<IdleEventData>>(json, JsonOptions);
                if (list != null) return list;
            }
            catch
            {
                // 무시하고 시도 계속
            }

            var result = new List<IdleEventData>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                foreach (var item in EnumerateArrayLike(root))
                {
                    var e = new IdleEventData
                    {
                        Id = GetElementString(item, "id", "_id"),
                        EmployeeId = GetElementString(item, "employeeId", "employee_id", "empNo", "emp_no"),
                        EmployeeName = GetElementString(item, "employeeName", "employee_name", "empName", "emp_name"),
                        ComputerName = GetElementString(item, "computerName", "computer_name", "pcName", "pc_name"),
                        ComputerIP = GetElementString(item, "computerIp", "computer_ip", "ip"),
                        IdleStartTime = GetElementString(item, "idleStartTime", "idle_start_time", "startTime", "start_time"),
                        IdleEndTime = GetElementString(item, "idleEndTime", "idle_end_time", "endTime", "end_time"),
                        ReasonCategory = GetElementString(item, "reasonCategory", "reason_category" , "category"),
                        ReasonDetail = GetElementString(item, "reasonDetail", "reason_detail", "detail"),
                        ReasonCode = GetElementString(item, "reasonCode", "reason_code"),
                        ReasonLevel1 = GetElementString(item, "reasonLevel1", "reason_level1"),
                        ReasonLevel2 = GetElementString(item, "reasonLevel2", "reason_level2"),
                        ReasonLevel3 = GetElementString(item, "reasonLevel3", "reason_level3")
                    };

                    result.Add(e);
                }
            }
            catch
            {
                // 파싱 실패 시 빈 리스트 반환
            }

            return result;
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return "IP Not Found";
        }

        private DateTime? PromptForDebugTime()
        {
#if DEBUG
            using var form = new Form
            {
                Text = "디버그용 현재 시각 설정",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(330, 140),
                MinimizeBox = false,
                MaximizeBox = false
            };

            var label = new Label
            {
                AutoSize = true,
                Text = "테스트용 기준 시각을 선택하세요.",
                Location = new Point(12, 15)
            };

            var picker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd HH:mm",
                ShowUpDown = true,
                Width = 200,
                Location = new Point(12, 40),
                Value = GetCurrentDateTime()
            };

            var okButton = new Button
            {
                Text = "확인",
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(150, 90)
            };

            var cancelButton = new Button
            {
                Text = "취소",
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(230, 90)
            };

            form.Controls.Add(label);
            form.Controls.Add(picker);
            form.Controls.Add(okButton);
            form.Controls.Add(cancelButton);

            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            return form.ShowDialog(this) == DialogResult.OK ? picker.Value : null;
#else
            return null;
#endif
        }
    }

    // DTOs inside namespace
    public class WorkTimeInfo
    {
        public string WorkStart { get; set; } = "09:30";
        public string WorkEnd { get; set; } = "17:30";
        public string PcShutdownTime { get; set; } = "17:30";
        public string LunchStart { get; set; } = "12:30";
        public string LunchEnd { get; set; } = "13:30";
        public int IdleThresholdMinutes { get; set; } = 10;
    }

    public class PcOffSettings
    {
        [JsonPropertyName("tempDisableCount")]
        public int TempDisableCount { get; set; } = 0;
        [JsonPropertyName("tempUsageMinutes")]
        public int TempUsageMinutes { get; set; } = 1;
        [JsonPropertyName("tempPopupImage")]
        public string TempPopupImage { get; set; } = string.Empty;
    }

    public class PcEventData
    {
        [JsonPropertyName("employeeId")] public string EmployeeId { get; set; } = "";
        [JsonPropertyName("employeeName")] public string EmployeeName { get; set; } = "";
        [JsonPropertyName("computerName")] public string ComputerName { get; set; } = "";
        [JsonPropertyName("computerIp")] public string ComputerIP { get; set; } = "";
        [JsonPropertyName("eventType")] public string EventType { get; set; } = "";
        [JsonPropertyName("eventTime")] public string EventTime { get; set; } = "";
    }

    public class ClientReleaseInfo
    {
        [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
        [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
        [JsonPropertyName("originalName")] public string OriginalName { get; set; } = string.Empty;
        [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = string.Empty;
        [JsonPropertyName("releaseNotes")] public string ReleaseNotes { get; set; } = string.Empty;
        [JsonPropertyName("uploadedAt")] public string UploadedAt { get; set; } = string.Empty;
    }

    public class ClientReleaseCheckResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; } = false;
        [JsonPropertyName("needUpdate")] public bool NeedUpdate { get; set; } = false;
        [JsonPropertyName("latest")] public ClientReleaseInfo? Latest { get; set; } = null;
        [JsonPropertyName("message")] public string? Message { get; set; } = string.Empty;
    }
}
