#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
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
        private bool isUpdatingClient;  // 자동 업데이트 진행 여부 플래그
        private Timer? userInfoRetryTimer; // 서버 미등록 시 사용자 정보 재입력 안내용
        private bool userInfoPromptScheduled;
        private readonly object userInfoPromptLock = new();
        private Random updateRandom;    // 업데이트 지연 랜덤 생성기

        // 업데이트 부하 분산 설정
        private const int UpdateCheckIntervalMinutes = 5;      // 업데이트 확인 주기 (분)
        private const int MaxUpdateCheckDelaySeconds = 300;    // 확인 전 최대 랜덤 지연 (초, 0~5분)
        private const int MaxUpdateDownloadJitterSeconds = 600; // 다운로드 전 최대 랜덤 지연 (초, 0~10분)
        private const int MaxUpdateDownloadRetries = 3;        // 다운로드 최대 재시도 횟수
        private const int UpdateRetryBaseDelaySeconds = 30;    // 재시도 기본 지연 (초, exponential backoff)
        private const int InstallerTimeoutMs = 5 * 60 * 1000;  // 설치 프로세스 최대 대기 시간 (5분)
        private const int LogFlushDelayMs = 100;               // 로그 플러시 대기 시간 (밀리초)
        private const string ApplicationName = "YEJI-On"; // 애플리케이션 이름

        // PC 종료 알림 관련 상수
        private const int OvertimeAlertMinutesBeforeEnd = 5;    // 연장근무 종료 몇 분 전에 알림 표시
        private const int AlertTimeChangeThresholdSeconds = 1;   // 알림 시각 변경 감지 임계값 (초)

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

        /// <summary>
        /// 절전/세션 복귀 시 실제 사용자 입력 vs 시스템 초기화 구분 임계값 (초).
        /// GetLastInputTime()이 resume/unlock 시각과 이 값보다 가까우면 시스템 초기화로 판단.
        /// Threshold in seconds to distinguish between actual user input and system initialization
        /// when resuming from suspend/sleep or unlocking session.
        /// </summary>
        private const int ResumeInputDetectionThresholdSeconds = 5;

        /// <summary>
        /// lastInputTime 보정 시 suspendStartTime보다 약간 이전으로 설정하기 위한 오프셋 (초).
        /// 이상 상태(lastInputTime > suspendStartTime) 발견 시 사용.
        /// Offset in seconds to adjust lastInputTime when an anomalous state is detected
        /// (lastInputTime later than suspendStartTime).
        /// </summary>
        private const int LastInputTimeAdjustmentOffsetSeconds = 1;

        private string employeeName;
        private string employeeId;
        private string computerName = Environment.MachineName;
        private string computerIP;

        private readonly HeartbeatWriter? heartbeatWriter; // Watcher 타임아웃 방지용

        private readonly string pendingIdleEventsFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "pending_idle_events.json");

        // client_config.json 저장 위치
        private readonly string clientConfigFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "client_config.json");

        // 영업금지 URL 캐시 파일
        private readonly string prohibitedUrlsCacheFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "prohibited_urls_cache.json");

        // 클라이언트 상태 전송 실패 기록용
        private readonly string clientStatusLogFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "client_status.log");

        // 마지막 실행된 클라이언트 버전 기록용
        private readonly string clientVersionFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "client_version.txt");

        // 업데이트 설치 후 재실행 시 완료 안내를 표시하기 위한 플래그 파일
        private readonly string pendingUpdateMarkerFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "pending_update.txt");

        // 마지막으로 시도한 업데이트 버전 기록 (중복 시도 방지)
        private readonly string lastAttemptedUpdateFile =
            Path.Combine(@"C:\ProgramData\YEJI_AW", "last_update_attempt.txt");

        private const string ServerBaseUrl = "http://175.106.99.157:3000";
        private const int UserInfoRetryDelayMs = 60 * 1000;

        private static string GetCurrentVersion()
        {
            try
            {
                string version = Application.ProductVersion;
                // 버전 정보에 +buildmetadata가 포함된 경우 제거 (예: 1.0.0+abcdef...)
                int plusIndex = version.IndexOf('+');
                if (plusIndex > 0)
                {
                    return version.Substring(0, plusIndex);
                }
                return version;
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

        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly TimeSpan heartbeatInterval = TimeSpan.FromMinutes(2);
        private readonly SemaphoreSlim heartbeatSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim idleIntervalSemaphore = new SemaphoreSlim(1, 1);
        private readonly TimeSpan popupCacheDuration = TimeSpan.FromMinutes(5);
        private DateTime lastPopupFetchUtc = DateTime.MinValue;
        private List<PopupSchedule> cachedPopupSchedules = new();
        private DateTime popupKeyDate;
        private const int PopupImageMinWidth = 720;
        private const int PopupImageMinHeight = 560;
        private HashSet<string> processedIdleIntervals = new();
        private List<(DateTime Start, DateTime End)> processedIdleIntervalRanges = new();
        private DateTime idleKeyDate;
        private bool hasShownPcOffAlert = false;
        private DateTime? pcOffAlertTargetTime;
        private readonly TimeSpan employeeOvertimeCheckInterval = TimeSpan.FromSeconds(60);
        private DateTime pcOffKeyDate;
        private DateTime? scheduledShutdownTime;
        
        private PcOffSettings pcOffSettings = new();
        private int remainingTempDisableCount;
        private DateTime lastPcOffSettingsFetchTime;
        private bool pcOffCountInitializedForDay;
        private Form? pcOffAlertForm;
        private Label? shutdownCountdownLabel;
        private Label? pcOffStatusLabel;
        private Form? shutdownCountdownTrayForm;
        private Label? shutdownCountdownTrayLabel;
        private bool isTemporaryDisableActive;
        private Form? tempDisableTrayForm;
        private Label? tempDisableRemainingLabel;
        private Label? tempDisableUsageLabel;
        private Timer? tempDisableTrayTimer;
        private DateTime? tempDisableEndTime;

        private bool isSystemShuttingDown; // Windows 종료 감지 플래그

        // 파일명 유효성 검사용 (성능 최적화)
        private static readonly HashSet<char> InvalidFileNameChars = new HashSet<char>(Path.GetInvalidFileNameChars());
        private static readonly HashSet<char> PathSeparators = new HashSet<char>(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });

        private Timer? managerNotificationTimer;
        private bool isCheckingManagerNotifications;
        private HashSet<string> lastAlertedManagerNotificationIds = new();
        private BalloonNotificationKind lastBalloonKind = BalloonNotificationKind.None;
        private DateTime lastBalloonShownAt = DateTime.MinValue;
        private ToolStripMenuItem? managerNotificationsMenuItem;
        private DateTime lastManagerNotificationAlertTime = DateTime.MinValue;
        private ManagerNotificationListForm? managerNotificationListForm;

        private bool isManagerUser;

        private Timer? employeeOvertimeStatusTimer;
        private bool isCheckingEmployeeOvertimeStatus;

        // URL 모니터링 관련
        private Timer? urlMonitorTimer;
        private List<string> prohibitedUrls = new();
        private List<BanUrlRow> prohibitedUrlRows = new();
        private string? previousUrl; // 이전 URL을 추적하여 변경 감지
        private string? lastAlertedUrl;
        private DateTime lastAlertedAt = DateTime.MinValue;
        private static readonly TimeSpan prohibitedAlertCooldown = TimeSpan.FromSeconds(15);
        private Dictionary<string, string> lastKnownOvertimeStatuses = new();
        
        private bool startupSequenceRunning;
        private bool startupSequenceNeedsRetry;
        private DateTime lastStartupRetryAttempt = DateTime.MinValue;
        private readonly TimeSpan startupRetryCooldown = TimeSpan.FromMinutes(1);

        private int taskbarCreatedMessageId;

        private static readonly TimeSpan BalloonClickWindow = TimeSpan.FromSeconds(10);

        private enum BalloonNotificationKind
        {
            None,
            Manager,
            General
        }

#if DEBUG
        private DateTime? debugBaseDateTime;
        private DateTime debugAnchorDateTime;
#endif

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern IntPtr OpenInputDesktop(int dwFlags, bool fInherit, int dwDesiredAccess);

        [DllImport("user32.dll")]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        private const int SM_CXSCREEN = 0;  // 실제 화면 너비 (DPI 스케일링 무시)
        private const int SM_CYSCREEN = 1;  // 실제 화면 높이 (DPI 스케일링 무시)

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

        public Form1(string employeeName, string employeeId, HeartbeatWriter? heartbeatWriter = null)
        {
            InitializeComponent();
            TrayIconCleanup.Register(notifyIcon);

            this.heartbeatWriter = heartbeatWriter;

            // 오래된 로그 파일 정리 (30일 이상)
            ClientLogger.CleanupOldLogs(30);

            ClientLogger.LogAgent($"Agent App Start... user={employeeId}, version={GetCurrentVersion()}.");

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

            // 설정(client_config) 및 영업금지 URL 갱신 타이머 (5분간격)
            configTimer = new Timer();
            configTimer.Interval = 5 * 60 * 1000; // 5분
            configTimer.Tick += async (s, e) =>
            {
                await FetchWorkTimeFromServerAsync();
                await FetchProhibitedUrlsAsync();
            };
            configTimer.Start();

            memoryTrimTimer = new Timer();
            memoryTrimTimer.Interval = 5 * 60 * 1000; // 5분마다 워킹셋 트리밍
            memoryTrimTimer.Tick += (s, e) => MemoryOptimizer.TrimWorkingSet();
            memoryTrimTimer.Start();

            heartbeatTimer = new Timer();
            heartbeatTimer.Interval = (int)heartbeatInterval.TotalMilliseconds;
            heartbeatTimer.Tick += async (s, e) => await SendHeartbeatAsync();

            // 업데이트 체크 주기를 5분으로 단축하여 빠른 업데이트 감지 가능
            // 단, 각 클라이언트마다 랜덤 지연을 추가하여 서버 부하 분산
            // 
            // 부하 분산 전략:
            // 1. 5분마다 업데이트 확인 (이전: 1시간)
            // 2. 확인 전 0~5분 랜덤 대기 (서버 요청 분산)
            // 3. 다운로드 전 0~10분 추가 지연 (다운로드 부하 분산)
            // 
            // 결과: 100명이 동시 업데이트 시도해도 ~20분에 걸쳐 분산됨
            updateCheckTimer = new Timer();
            updateCheckTimer.Interval = (int)TimeSpan.FromMinutes(UpdateCheckIntervalMinutes).TotalMilliseconds;
            updateCheckTimer.Tick += async (s, e) => await CheckClientUpdateAsync();

            // 랜덤 시드를 컴퓨터 이름과 사용자 ID 조합으로 설정하여 재시작 시 일관성 유지
            // 각 클라이언트마다 고유한 시드로 공평한 부하 분산
            int seed = (computerName + employeeId).GetHashCode();
            updateRandom = new Random(seed);

            employeeOvertimeStatusTimer = new Timer();
            employeeOvertimeStatusTimer.Interval = (int)employeeOvertimeCheckInterval.TotalMilliseconds;
            employeeOvertimeStatusTimer.Tick += async (s, e) => await CheckEmployeeOvertimeStatusAsync();

            // URL 모니터링 타이머 (1초마다 체크하여 빠른 감지)
            urlMonitorTimer = new Timer();
            urlMonitorTimer.Interval = 1000; // 1초 - 도메인 접속 시 즉시 감지하기 위해 짧은 간격 사용
            urlMonitorTimer.Tick += (s, e) => CheckBrowserUrl();

            this.Load += (s, e) =>
            {
                this.Hide();
                this.ShowInTaskbar = false;
                ShowUpdateCompletionNotificationIfNeeded();               
                heartbeatTimer.Start();
                updateCheckTimer.Start();
                employeeOvertimeStatusTimer.Start();
                urlMonitorTimer?.Start();
                _ = RunStartupSequenceAsync();
            };

            InitializeTrayMenu();
            notifyIcon.BalloonTipClicked += async (s, e) => await OnManagerNotificationBalloonClickedAsync();

            // 종료 시 SHUTDOWN 이벤트 전송
            this.FormClosing += Form1_FormClosing;

            taskbarCreatedMessageId = RegisterWindowMessage("TaskbarCreated");

            // 프로그램 시작 시 이벤트 전송:
            // - PC_ON: Windows 시스템 부팅 시각 (GetSystemBootTime 사용)
            // - LOGIN: 클라이언트 프로그램 시작 시각 (현재 시각)
            // (예외는 각 메서드 내부에서 처리됨)
            _ = Task.WhenAll(SendPcEventAsync("PC_ON"), SendPcEventAsync("LOGIN")).ConfigureAwait(false);

            // 전원(절전/복귀) 이벤트 구독
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            // 세션 잠금/해제 이벤트 구독 (Windows+L 등)
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            // Windows 시스템 종료 이벤트 구독
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;

            // 네트워크 복구 시 초기화 재시도
            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;

            // 앱 시작 시 세션이 이미 잠겨있는지 확인
            // (사용자가 화면을 잠근 상태에서 앱이 재시작된 경우)
            if (IsSessionLocked())
            {
                sessionLockStartTime = GetCurrentDateTime();
                ClientLogger.LogAgent($"Session is locked at app start, initializing sessionLockStartTime to {sessionLockStartTime:HH:mm:ss}.", "DBG");
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (taskbarCreatedMessageId == 0)
            {
                taskbarCreatedMessageId = RegisterWindowMessage("TaskbarCreated");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == taskbarCreatedMessageId)
            {
                RestoreNotifyIcon();
            }

            base.WndProc(ref m);
        }

        // 폼 완전 종료 시 전원 이벤트 구독 해제
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            SystemEvents.SessionEnding -= SystemEvents_SessionEnding;
            NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
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

        // -----------------------------
        // 영업금지 URL 목록 가져오기 (증분 동기화)
        // -----------------------------
        private async Task FetchProhibitedUrlsAsync()
        {
            try
            {
                // 로컬 캐시 로드
                var cache = LoadProhibitedUrlsCache();

                // since 파라미터 설정 (마지막 동기화 시각)
                string? sinceParam = cache?.LastSyncTime;

                // API 호출
                var client = HttpClient;
                string url = $"{ServerBaseUrl}/api/client/ban-urls?include_deleted=1";
                if (!string.IsNullOrWhiteSpace(sinceParam))
                {
                    url += $"&since={Uri.EscapeDataString(sinceParam)}";
                }

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                var apiResponse = await JsonSerializer.DeserializeAsync<BanUrlsResponse>(stream, JsonOptions);

                if (apiResponse != null && apiResponse.Success)
                {
                    // 캐시 업데이트
                    if (cache == null)
                    {
                        cache = new ProhibitedUrlsCache();
                    }

                    // 서버에서 리셋 요청이 온 경우 또는 reset_at이 LastSyncTime보다 최신인 경우 로컬 캐시 초기화
                    bool needsReset = apiResponse.ResetRequired;

                    // reset_at 타임스탬프가 있고 LastSyncTime이 있는 경우, 타임스탬프를 비교하여 리셋 필요 여부 확인
                    if (!needsReset && !string.IsNullOrWhiteSpace(apiResponse.ResetAt) && !string.IsNullOrWhiteSpace(cache.LastSyncTime))
                    {
                        if (DateTime.TryParse(apiResponse.ResetAt, out DateTime resetTime) &&
                            DateTime.TryParse(cache.LastSyncTime, out DateTime lastSyncTime))
                        {
                            if (resetTime > lastSyncTime)
                            {
                                needsReset = true;
                                ClientLogger.LogAgent($"Reset detected: reset_at ({apiResponse.ResetAt}) > LastSyncTime ({cache.LastSyncTime})", "DBG");
                            }
                        }
                    }

                    if (needsReset)
                    {
                        cache.Urls.Clear();
                        cache.LastSyncTime = null; // 초기화 시 LastSyncTime도 초기화하여 다음 요청 시 전체 데이터를 가져오도록 함
                        ClientLogger.LogAgent("Cleared prohibited URLs cache due to server reset.", "DBG");
                    }

                    // 성능 개선: Dictionary를 사용하여 O(1) 조회
                    var urlDict = cache.Urls.ToDictionary(u => u.Url, u => u);

                    // 새로운 URL 추가/업데이트 및 삭제 처리
                    if (apiResponse.Rows != null && apiResponse.Rows.Count > 0)
                    {
                        foreach (var row in apiResponse.Rows)
                        {
                            if (!string.IsNullOrWhiteSpace(row.Url))
                            {
                                if (row.Deleted)
                                {
                                    // 삭제된 항목은 로컬 캐시에서 제거
                                    if (urlDict.TryGetValue(row.Url, out var toDelete))
                                    {
                                        cache.Urls.Remove(toDelete);
                                        urlDict.Remove(row.Url);
                                    }
                                }
                                else
                                {
                                    // 삭제되지 않은 항목은 업서트
                                    if (urlDict.TryGetValue(row.Url, out var existing))
                                    {
                                        // 기존 항목 업데이트
                                        existing.CompanyName = row.CompanyName;
                                        existing.UpdatedAt = row.UpdatedAt;
                                        existing.Deleted = false;
                                    }
                                    else
                                    {
                                        // 새 항목 추가
                                        cache.Urls.Add(row);
                                        urlDict[row.Url] = row;
                                    }
                                }
                            }
                        }
                    }

                    // 동기화 시각 업데이트
                    // 리셋 요청이 있었던 경우 LastSyncTime을 null로 유지하여 다음 요청 시 전체 데이터를 가져오도록 함
                    if (!needsReset)
                    {
                        if (!string.IsNullOrWhiteSpace(apiResponse.NextSince))
                        {
                            cache.LastSyncTime = apiResponse.NextSince;
                        }
                        else
                        {
                            cache.LastSyncTime = apiResponse.ServerTime;
                            ClientLogger.LogAgent("NextSince is null, using ServerTime for sync timestamp.", "DBG");
                        }
                    }

                    // 캐시 저장
                    SaveProhibitedUrlsCache(cache);

                    // 메모리에 URL 목록 적용
                    prohibitedUrls = ExtractUrlsFromCache(cache);
                    prohibitedUrlRows = new List<BanUrlRow>(cache.Urls);

                    ClientLogger.LogAgent($"Synced prohibited URLs: {apiResponse.Count} changes, total {prohibitedUrls.Count} URLs in cache.", "DBG");
                }
            }
            catch (Exception ex)
            {
                // 동기화 실패 시 로컬 캐시에서 로드
                ClientLogger.LogAgent($"Failed to sync prohibited URLs: {ex.Message}", "DBG");

                var cache = LoadProhibitedUrlsCache();
                if (cache != null && cache.Urls.Count > 0)
                {
                    prohibitedUrls = ExtractUrlsFromCache(cache);
                    prohibitedUrlRows = new List<BanUrlRow>(cache.Urls);
                    ClientLogger.LogAgent($"Loaded {prohibitedUrls.Count} prohibited URLs from local cache.", "DBG");
                }
            }
        }

        private static List<string> ExtractUrlsFromCache(ProhibitedUrlsCache cache)
        {
            return cache.Urls.Select(u => u.Url).ToList();
        }

        private ProhibitedUrlsCache? LoadProhibitedUrlsCache()
        {
            try
            {
                if (!File.Exists(prohibitedUrlsCacheFile))
                {
                    return null;
                }

                string json = File.ReadAllText(prohibitedUrlsCacheFile, Encoding.UTF8);
                return JsonSerializer.Deserialize<ProhibitedUrlsCache>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                ClientLogger.LogAgent($"Failed to load prohibited URLs cache: {ex.Message}", "DBG");
                return null;
            }
        }

        private void SaveProhibitedUrlsCache(ProhibitedUrlsCache cache)
        {
            try
            {
                string? folder = Path.GetDirectoryName(prohibitedUrlsCacheFile);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string json = JsonSerializer.Serialize(cache, JsonOptions);
                File.WriteAllText(prohibitedUrlsCacheFile, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ClientLogger.LogAgent($"Failed to save prohibited URLs cache: {ex.Message}", "Err");
            }
        }

        private async Task RunStartupSequenceAsync()
        {
            if (startupSequenceRunning)
            {
                return;
            }

            startupSequenceRunning = true;
            startupSequenceNeedsRetry = false;
            lastStartupRetryAttempt = DateTime.Now;

            try
            {
                startupSequenceNeedsRetry |= !await ExecuteStartupStepAsync("ResendPendingIdleEvents", ResendPendingIdleEventsAsync);
                startupSequenceNeedsRetry |= !await ExecuteStartupStepAsync("FetchWorkTimeFromServer", FetchWorkTimeFromServerAsync);
                startupSequenceNeedsRetry |= !await ExecuteStartupStepAsync("FetchProhibitedUrls", FetchProhibitedUrlsAsync);
                startupSequenceNeedsRetry |= !await ExecuteStartupStepAsync("CheckClientUpdate", () => CheckClientUpdateAsync());
                startupSequenceNeedsRetry |= !await ExecuteStartupStepAsync("RegisterOrUpdateClient", RegisterOrUpdateClientAsync);
                startupSequenceNeedsRetry |= !await ExecuteStartupStepAsync("CheckAfterHoursOnStartup", CheckAfterHoursOnStartupAsync);
            }
            finally
            {
                startupSequenceRunning = false;
            }
        }

        private async Task<bool> ExecuteStartupStepAsync(string stepName, Func<Task> step)
        {
            try
            {
                await step();
                return true;
            }
            catch (Exception ex)
            {
                ClientLogger.LogAgent($"Startup step failed: {stepName}.", "Err", ex);
                return false;
            }
        }

        private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (!e.IsAvailable)
            {
                return;
            }

            if (!startupSequenceNeedsRetry || startupSequenceRunning)
            {
                return;
            }

            if (DateTime.Now - lastStartupRetryAttempt < startupRetryCooldown)
            {
                return;
            }

            _ = RunStartupSequenceAsync();
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

            // 영업금지 URL 목록 적용
            prohibitedUrls = info.ProhibitedUrls ?? new List<string>();
            prohibitedUrlRows = new List<BanUrlRow>();
            ClientLogger.LogAgent($"Loaded {prohibitedUrls.Count} prohibited URLs from configuration.", "DBG");
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
        private async Task CheckClientUpdateAsync(bool forceInstall = false, bool notifyWhenNoUpdate = false)
        {
            try
            {
                string currentVersion = GetCurrentVersion();

                // 강제 설치가 아닌 경우 랜덤 지연 추가
                // 서버 부하를 분산시키기 위해 각 클라이언트가 다른 시점에 체크하도록 함
                // 단, 첫 번째 체크 시에는 업데이트 우선순위를 먼저 확인
                if (!forceInstall)
                {
                    int checkDelaySeconds = updateRandom.Next(0, MaxUpdateCheckDelaySeconds + 1);
                    ClientLogger.LogUpdate($"Applying random delay of {checkDelaySeconds}s before update check (max {MaxUpdateCheckDelaySeconds}s).", "DBG");
                    await Task.Delay(TimeSpan.FromSeconds(checkDelaySeconds));
                }
                ClientLogger.LogUpdate($"Checking client update (current {currentVersion}).", "DBG");
                string url = $"{ServerBaseUrl}/api/client-releases/check?currentVersion={Uri.EscapeDataString(currentVersion)}";

                using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                var checkResult = await JsonSerializer.DeserializeAsync<ClientReleaseCheckResponse>(stream, JsonOptions);

                bool checkSuccess = checkResult?.Success == true;
                bool hasLatestRelease = checkResult?.Latest != null;
                bool shouldUpdate = checkSuccess && hasLatestRelease && (checkResult!.NeedUpdate || forceInstall);

                if (shouldUpdate)
                {
                    if (isUpdatingClient)
                    {
                        ClientLogger.LogUpdate("Update already in progress. Skipping duplicate check.", "DBG");
                        return;
                    }

                    string latestVersion = checkResult!.Latest!.Version;

                    // 중복 업데이트 시도 방지: 최근에 이미 같은 버전 설치를 시도했는지 확인
                    if (!forceInstall && HasRecentlyAttemptedUpdate(latestVersion))
                    {
                        ClientLogger.LogUpdate($"Update to {latestVersion} was recently attempted. Skipping to prevent duplicate installation.", "DBG");
                        return;
                    }

                    string priority = string.IsNullOrWhiteSpace(checkResult.Latest.Priority)
                       ? "normal"
                       : checkResult.Latest.Priority.Trim();
                    bool isUrgent = string.Equals(priority, "urgent", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(priority, "high", StringComparison.OrdinalIgnoreCase);

                    string notificationTitle = forceInstall ? "디버그 업데이트 테스트" :
                                             isUrgent ? "긴급 보안 업데이트" : "자동 업데이트 진행 중";
                    string notificationMessage = forceInstall
                        ? $"디버그 모드에서 새 버전({latestVersion}) 설치를 강제로 시작합니다."
                        : isUrgent
                        ? $"긴급 업데이트({latestVersion})를 즉시 설치합니다."
                        : $"새 버전({latestVersion})이 감지되어 자동으로 설치를 진행합니다.";

                    ClientLogger.LogUpdate($"Update required. Latest {latestVersion} (priority: {priority}) detected. ForceInstall={forceInstall}.");
                    ShowTrayNotification(notificationTitle, notificationMessage);
                    await DownloadAndInstallUpdateAsync(checkResult.Latest, bypassDelay: isUrgent || forceInstall);
                }
                else if (checkSuccess)
                {
                    ClientLogger.LogUpdate("No update required.", "DBG");

                    if (notifyWhenNoUpdate)
                    {
                        string message = hasLatestRelease
                            ? $"업데이트 대상이 없어 설치를 건너뜁니다. (서버 최신: {checkResult!.Latest!.Version})"
                            : "서버에 최신 릴리스 정보가 없어 업데이트를 건너뜁니다.";
                        ShowTrayNotification("디버그 업데이트 테스트", message, ToolTipIcon.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                ClientLogger.LogUpdate("Client update check failed.", "Err", ex);
            }
        }

        private void ShowUpdateCompletionNotificationIfNeeded()
        {
            try
            {
                string currentVersion = GetCurrentVersion();
                ClientLogger.LogUpdate($"Checking for update completion. Current version: {currentVersion}", "DBG");

                string folder = Path.GetDirectoryName(clientVersionFile) ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string? previousVersion = null;
                string? pendingUpdateVersion = null;
                bool hasPendingUpdateMarker = false;

                if (File.Exists(clientVersionFile))
                {
                    previousVersion = File.ReadAllText(clientVersionFile, Encoding.UTF8).Trim();
                    ClientLogger.LogUpdate($"Previous version from file: {previousVersion}", "DBG");
                }
                else
                {
                    ClientLogger.LogUpdate("No previous version file found (first run or file deleted).", "DBG");
                }

                if (File.Exists(pendingUpdateMarkerFile))
                {
                    pendingUpdateVersion = File.ReadAllText(pendingUpdateMarkerFile, Encoding.UTF8).Trim();
                    hasPendingUpdateMarker = true;
                    ClientLogger.LogUpdate($"Pending update marker found for version: {pendingUpdateVersion}", "DBG");
                }
                else
                {
                    ClientLogger.LogUpdate("No pending update marker found.", "DBG");
                }

                File.WriteAllText(clientVersionFile, currentVersion, Encoding.UTF8);
                ClientLogger.LogUpdate($"Current version saved to file: {currentVersion}", "DBG");

                bool updatedFromPrevious = !string.IsNullOrWhiteSpace(previousVersion) && previousVersion != currentVersion;
                bool updatedFromPending = !string.IsNullOrWhiteSpace(pendingUpdateVersion) && pendingUpdateVersion == currentVersion;

                ClientLogger.LogUpdate($"Update detection: updatedFromPrevious={updatedFromPrevious}, updatedFromPending={updatedFromPending}", "DBG");

                if (updatedFromPrevious || updatedFromPending)
                {
                    ClientLogger.LogUpdate($"UPDATE COMPLETED SUCCESSFULLY: {previousVersion ?? "unknown"} -> {currentVersion}");
                    ShowTrayNotification(
                       "업데이트 완료",
                       $"클라이언트가 최신 버전({currentVersion})으로 업데이트되었습니다.");
                }
                else
                {
                    ClientLogger.LogUpdate("No version change detected (normal startup).", "DBG");
                }

                if (hasPendingUpdateMarker)
                {
                    if (updatedFromPrevious || updatedFromPending)
                    {
                        File.Delete(pendingUpdateMarkerFile);
                        ClientLogger.LogUpdate("Pending update marker deleted.", "DBG");
                    }
                    else
                    {
                        ClientLogger.LogUpdate($"Pending update marker retained. Current version {currentVersion} does not match pending {pendingUpdateVersion ?? "unknown"}.", "DBG");
                    }
                }
            }
            catch (Exception ex)
            {
                ClientLogger.LogUpdate("Failed to check/notify update completion.", "Err", ex);
            }
        }

        private void ShowTrayNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 5000)
        {
            if (notifyIcon == null)
            {
                return;
            }

            lastBalloonKind = BalloonNotificationKind.General;
            lastBalloonShownAt = DateTime.Now;
            notifyIcon.BalloonTipTitle = title;
            notifyIcon.BalloonTipText = message;
            notifyIcon.BalloonTipIcon = icon;
            notifyIcon.ShowBalloonTip(timeoutMs);
        }

        private async Task DownloadAndInstallUpdateAsync(ClientReleaseInfo latestRelease, bool bypassDelay = false)
        {
            isUpdatingClient = true;
            string targetVersion = latestRelease.Version;

            ClientLogger.LogUpdate("=== UPDATE PROCESS STARTED ===");
            ClientLogger.LogUpdate($"Target version: {targetVersion}");
            ClientLogger.LogUpdate($"Current version: {GetCurrentVersion()}");
            ClientLogger.LogUpdate($"Bypass delay: {bypassDelay}");
            ClientLogger.LogUpdate($"Download URL: {latestRelease.DownloadUrl}");
            ClientLogger.LogUpdate($"File name: {latestRelease.FileName}");

            // 업데이트 시도 기록 (중복 시도 방지)
            RecordUpdateAttempt(targetVersion);

            // 다운로드 시작 전 추가 랜덤 지연
            // 업데이트 체크 시점이 비슷하더라도 다운로드 시점을 분산시켜 서버 부하 방지
            // 긴급/보안 업데이트는 지연 없이 즉시 다운로드
            if (!bypassDelay)
            {
                int downloadDelaySeconds = updateRandom.Next(0, MaxUpdateDownloadJitterSeconds + 1);
                ClientLogger.LogUpdate($"Applying random jitter of {downloadDelaySeconds}s before download (max {MaxUpdateDownloadJitterSeconds}s).", "DBG");
                await Task.Delay(TimeSpan.FromSeconds(downloadDelaySeconds));
            }
            else
            {
                ClientLogger.LogUpdate("Bypassing download delay for urgent/forced update.", "DBG");
            }
                        
            string downloadUrl = latestRelease.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? latestRelease.DownloadUrl
                : ServerBaseUrl + latestRelease.DownloadUrl;

            // 서버에서 받은 파일명을 정제하여 경로 순회 공격 및 잘못된 문자 방지
            string rawFileName = string.IsNullOrWhiteSpace(latestRelease.FileName)
                ? $"YEJI-On_{latestRelease.Version}.exe"
                : latestRelease.FileName;

            string targetFileName = SanitizeFileName(rawFileName);

            // 파일명이 정제되어 원본과 달라진 경우 디버그 로그 남김
            if (!string.Equals(rawFileName, targetFileName, StringComparison.Ordinal))
            {
                ClientLogger.LogUpdate($"Filename sanitized: '{rawFileName}' -> '{targetFileName}'", "WARN");
            }

            // 경로 구분자가 남아있는지 최종 검증 (이중 방어)
            if (targetFileName.Any(c => PathSeparators.Contains(c)))
            {
                ClientLogger.LogUpdate($"Filename still contains path separators after sanitization: '{targetFileName}'. Using safe default.", "ERR");
                targetFileName = $"YEJI-On_Setup_{DateTime.UtcNow:yyyyMMddHHmmss}.exe";
            }

            // .exe 확장자가 없으면 추가 (보안: 실행 파일만 허용)
            if (!targetFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ClientLogger.LogUpdate($"Filename missing .exe extension: '{targetFileName}'. Appending .exe", "WARN");
                targetFileName += ".exe";
            }

            string tempFilePath = Path.Combine(Path.GetTempPath(), targetFileName);
            ClientLogger.LogUpdate($"Final download path: {tempFilePath}", "DBG");

            // 재시도 로직: 실패 시 exponential backoff
            bool downloadSuccess = false;
            for (int attempt = 1; attempt <= MaxUpdateDownloadRetries; attempt++)
            {
                try
                {
                    ClientLogger.LogUpdate($"Download attempt {attempt}/{MaxUpdateDownloadRetries} for {latestRelease.Version}.", "DBG");
                    ClientLogger.LogUpdate($"Downloading from: {downloadUrl}", "DBG");

                    using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long? contentLength = response.Content.Headers.ContentLength;
                    ClientLogger.LogUpdate($"Response status: {response.StatusCode}, Content-Length: {contentLength?.ToString() ?? "unknown"}", "DBG");

                    await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fileStream);
                    
                    downloadSuccess = true;
                    ClientLogger.LogUpdate($"Download completed successfully. File saved to: {tempFilePath}", "DBG");
                    break; // 성공 시 루프 탈출
                }
                catch (Exception ex)
                {
                    ClientLogger.LogUpdate($"Download attempt {attempt}/{MaxUpdateDownloadRetries} failed for {latestRelease.Version}.", "Err", ex);

                    if (attempt == MaxUpdateDownloadRetries)
                    {
                        // 마지막 시도 실패 시 에러 처리
                        ClientLogger.LogUpdate("All download attempts failed. Aborting update.", "ERR");
                        ShowTrayNotification("업데이트 실패", "업데이트 파일 다운로드에 실패했습니다. 네트워크 연결을 확인한 뒤 다시 시도해주세요.", ToolTipIcon.Error);
                        isUpdatingClient = false;
                        return;
                    }

                    // 재시도 전 대기 (exponential backoff: 30s, 60s, 120s, ...)
                    // 비트 시프트 사용으로 정수 오버플로우 방지, 최대 5분으로 제한
                    int waitSeconds = Math.Min(UpdateRetryBaseDelaySeconds << (attempt - 1), 300);
                    ClientLogger.LogUpdate($"Retrying download after {waitSeconds}s delay (exponential backoff).", "DBG");
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
                }
            }

            // 다운로드 완료 후 파일 존재 및 크기 검증
            ClientLogger.LogUpdate("Validating downloaded file...", "DBG");

            if (!File.Exists(tempFilePath))
            {
                ClientLogger.LogUpdate($"VALIDATION FAILED: Downloaded file does not exist at path: {tempFilePath}", "ERR");
                ShowTrayNotification("업데이트 실패", "다운로드한 파일을 찾을 수 없습니다.", ToolTipIcon.Error);
                isUpdatingClient = false;
                return;
            }

            var fileInfo = new FileInfo(tempFilePath);
            if (fileInfo.Length == 0)
            {
                ClientLogger.LogUpdate($"VALIDATION FAILED: Downloaded file is empty (0 bytes): {tempFilePath}", "ERR");
                ShowTrayNotification("업데이트 실패", "다운로드한 파일이 비어있습니다.", ToolTipIcon.Error);
                isUpdatingClient = false;
                return;
            }

            ClientLogger.LogUpdate($"VALIDATION SUCCESS: File size: {fileInfo.Length:N0} bytes, Path: {tempFilePath}");

            try
            {
                CreatePendingUpdateMarker(targetVersion);

                bool isExecutable = string.Equals(Path.GetExtension(tempFilePath), ".exe", StringComparison.OrdinalIgnoreCase);
                ClientLogger.LogUpdate($"File extension check: isExecutable={isExecutable}", "DBG");

                // Inno Setup 자동 설치 플래그: 설치 UI를 표시하지 않고 백그라운드에서 자동 설치
                // /VERYSILENT: 모든 설치 UI 숨김 (진행률 대화상자 포함)
                // /SUPPRESSMSGBOXES: 모든 메시지 박스 억제
                // /NORESTART: 설치 후 시스템 재시작 안 함
                // /SP-: "준비 중..." 페이지 생략
                //
                // 주의: /CLOSEAPPLICATIONS 및 /RESTARTAPPLICATIONS 플래그 제거됨
                // 이유: 앱이 직접 인스톨러를 실행하고 대기하는 경우, 인스톨러가 앱을 종료하려고 하면 교착 상태 발생
                // 대신: 인스톨러 실행 후 즉시 앱 종료, ISS 파일의 CurStepChanged에서 새 버전 자동 시작
                //
                // 주의: /NOCANCEL 플래그도 제거됨
                // 이유: /SUPPRESSMSGBOXES와 함께 사용 시 오류가 발생해도 메시지를 표시할 수 없어 조기 종료됨

                // 버전 문자열에서 파일명에 사용할 수 없는 문자 제거 (보안)
                string sanitizedVersion = SanitizeFileName(targetVersion);

                // 경로 순회 공격 방지: 디렉터리 구분자가 포함되지 않도록 추가 검증
                if (sanitizedVersion.Any(c => PathSeparators.Contains(c)))
                {
                    sanitizedVersion = "unknown";
                }

                string logPath = Path.Combine(Path.GetTempPath(), $"YEJI-On_Setup_{sanitizedVersion}.log");

                // Inno Setup은 경로에 따옴표가 포함된 경우 \" 이스케이프 처리가 필요
                // 버전 문자열은 이미 sanitize되고 임시 폴더 경로는 시스템 제어이므로 안전
                // 추가 보안: 경로를 따옴표로 감싸 공백 등의 특수문자 처리
                string arguments = isExecutable
                    ? $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=\"{logPath}\""
                    : string.Empty;

                ClientLogger.LogUpdate("=== INSTALLER LAUNCH CONFIGURATION ===");
                ClientLogger.LogUpdate($"Executable path: {tempFilePath}");
                ClientLogger.LogUpdate($"File exists: {File.Exists(tempFilePath)}");
                ClientLogger.LogUpdate($"File size: {new FileInfo(tempFilePath).Length:N0} bytes");
                ClientLogger.LogUpdate($"Is executable: {isExecutable}");
                ClientLogger.LogUpdate($"Arguments: {arguments}");
                ClientLogger.LogUpdate($"Setup log path: {logPath}");
                ClientLogger.LogUpdate($"Current process will exit immediately after launch");

                var startInfo = new ProcessStartInfo
                {
                    FileName = tempFilePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    // 설치 프로세스를 백그라운드에서 실행
                    // Hidden 대신 Minimized 사용하여 프로세스가 정상 실행되도록 함
                    WindowStyle = ProcessWindowStyle.Minimized
                };

                ClientLogger.LogUpdate("Launching installer process...");
                var installerProcess = Process.Start(startInfo);

                if (installerProcess == null)
                {
                    throw new InvalidOperationException("Process.Start returned null - installer failed to launch.");
                }

                ClientLogger.LogUpdate($"INSTALLER LAUNCHED: PID={installerProcess.Id}, ProcessName={installerProcess.ProcessName}");
                ClientLogger.LogUpdate($"Installation will proceed in background. Current application will now exit.");
                ClientLogger.LogUpdate($"After installation, new version should auto-start via Inno Setup script.");
                ClientLogger.LogUpdate($"To troubleshoot installation issues, check: {logPath}");
                ClientLogger.LogUpdate("=== EXITING APPLICATION FOR INSTALLATION ===");

                // 중요: 인스톨러 실행 후 즉시 현재 앱 종료
                // ISS 파일의 CurStepChanged가 설치 완료 후 새 버전을 자동으로 시작함
                // 
                // Environment.Exit(0) 사용하는 이유:
                // - 프로세스를 즉시 강제 종료하여 인스톨러가 .exe 파일을 교체할 수 있도록 함
                // - Application.Exit()는 메시지 루프를 통한 정상 종료로 FormClosing 이벤트가 실행되고
                //   async 작업이 있을 경우 종료가 지연될 수 있어 인스톨러가 파일 교체 실패 (Error code 5)
                // - 0은 정상 종료를 의미하는 exit code
                // 
                // 참고: Form1_FormClosing에서 isUpdatingClient 플래그로 LOGOUT 이벤트를 건너뛰지만,
                // Environment.Exit(0)는 FormClosing 이벤트를 전혀 발생시키지 않아 더 빠르고 확실함

                // 로그가 디스크에 완전히 기록될 시간을 확보하기 위한 짧은 지연
                // ClientLogger.LogUpdate는 File.AppendAllText를 사용하여 동기적으로 기록하지만,
                // 운영체제 파일 시스템 캐시에서 디스크로 실제 쓰기가 완료되는 시간을 고려
                // Thread.Sleep 사용: Environment.Exit(0) 전에 확실히 완료되도록 보장
                Thread.Sleep(LogFlushDelayMs);

                ClientLogger.LogUpdate("Calling Environment.Exit(0) now...");

                // 이 시점 이후로는 코드가 실행되지 않음 - 프로세스 즉시 종료
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                ClientLogger.LogUpdate("INSTALLER LAUNCH FAILED", "ERR", ex);

                ShowTrayNotification(
                  "업데이트 실행 실패",
                  "설치 파일 실행 중 오류가 발생했습니다.\n\n시스템 관리자에게 문의하세요.",
                  ToolTipIcon.Error,
                  timeoutMs: 10000);
                if (File.Exists(pendingUpdateMarkerFile))
                {
                    File.Delete(pendingUpdateMarkerFile);
                }
                isUpdatingClient = false;
            }
        }
        
        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "unknown";
            }

            // 매우 긴 파일명의 경우 스택 오버플로우 방지 (Windows MAX_PATH 기준)
            const int MaxFileNameLength = 260;
            if (fileName.Length > MaxFileNameLength)
            {
                fileName = fileName.Substring(0, MaxFileNameLength);
            }

            // 파일명에 사용할 수 없는 문자 및 경로 구분자를 제거
            // 정적 HashSet 사용으로 반복 생성 방지 및 O(1) 조회 성능 확보
            // Span 기반 처리로 성능 최적화
            Span<char> buffer = stackalloc char[fileName.Length];
            int writeIndex = 0;
            bool hasPathSeparator = false;

            foreach (char c in fileName)
            {
                // 경로 구분자 검사를 동시에 수행하여 두 번째 반복 제거
                if (PathSeparators.Contains(c))
                {
                    hasPathSeparator = true;
                    continue;
                }

                if (!InvalidFileNameChars.Contains(c))
                {
                    buffer[writeIndex++] = c;
                }
            }

            // 경로 순회 공격 감지
            if (hasPathSeparator)
            {
                return "unknown";
            }

            string sanitized = new string(buffer.Slice(0, writeIndex));

            // 결과가 비어있거나 점만 있는 경우 기본값 반환 (숨김 파일 방지)
            return string.IsNullOrWhiteSpace(sanitized) || sanitized == "." || sanitized == ".."
                ? "unknown"
                : sanitized;
        }

        private static string GetInstallerErrorMessage(int exitCode)
        {
            // Inno Setup 설치 프로그램의 종료 코드를 사용자 친화적인 메시지로 변환
            return exitCode switch
            {
                1 => "설치가 취소되었습니다.",
                2 => "치명적 오류가 발생했습니다.",
                3 => "설치 파일이 손상되었습니다.",
                4 => "잘못된 설치 매개변수입니다.",
                5 => "파일 교체 중 액세스가 거부되었습니다. 관리자 권한으로 재시도하거나 실행 중인 프로그램을 종료해주세요.",
                _ => $"알 수 없는 오류가 발생했습니다 (코드: {exitCode})."
            };
        }
        
        private void CreatePendingUpdateMarker(string targetVersion)
        {
            try
            {
                string folder = Path.GetDirectoryName(pendingUpdateMarkerFile) ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.WriteAllText(pendingUpdateMarkerFile, targetVersion, Encoding.UTF8);
                ClientLogger.LogUpdate($"Pending update marker created for version {targetVersion}.", "DBG");
            }
            catch (Exception ex)
            {
                ClientLogger.LogUpdate("Failed to create pending update marker.", "Err", ex);
            }
        }

        private bool HasRecentlyAttemptedUpdate(string version)
        {
            try
            {
                if (File.Exists(pendingUpdateMarkerFile))
                {
                    string pendingVersion = File.ReadAllText(pendingUpdateMarkerFile, Encoding.UTF8).Trim();
                    string currentVersion = GetCurrentVersion();

                    if (!string.IsNullOrWhiteSpace(pendingVersion) &&
                        !string.Equals(pendingVersion, currentVersion, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(pendingVersion, version, StringComparison.OrdinalIgnoreCase))
                    {
                        ClientLogger.LogUpdate($"Pending update {pendingVersion} detected while current version is {currentVersion}. Allowing retry despite recent attempt.", "DBG");
                        return false;
                    }
                }

                if (!File.Exists(lastAttemptedUpdateFile))
                {
                    return false;
                }

                var lines = File.ReadAllLines(lastAttemptedUpdateFile, Encoding.UTF8);
                if (lines.Length < 2)
                {
                    return false;
                }

                string attemptedVersion = lines[0].Trim();
                if (!DateTime.TryParse(lines[1].Trim(), out DateTime attemptTime))
                {
                    return false;
                }

                // 같은 버전이고 30분 이내에 시도했다면 중복 시도로 판단
                bool isSameVersion = string.Equals(attemptedVersion, version, StringComparison.OrdinalIgnoreCase);
                bool isRecent = (DateTime.Now - attemptTime).TotalMinutes < 30;

                if (isSameVersion && isRecent)
                {
                    ClientLogger.LogUpdate($"Recent update attempt found: version={attemptedVersion}, attempted={attemptTime:yyyy-MM-dd HH:mm:ss}, {(DateTime.Now - attemptTime).TotalMinutes:F1} minutes ago.", "DBG");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                ClientLogger.LogUpdate("Failed to check recent update attempt.", "Err", ex);
                return false;
            }
        }

        private void RecordUpdateAttempt(string version)
        {
            try
            {
                string folder = Path.GetDirectoryName(lastAttemptedUpdateFile) ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var content = $"{version}{Environment.NewLine}{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                File.WriteAllText(lastAttemptedUpdateFile, content, Encoding.UTF8);
                ClientLogger.LogUpdate($"Recorded update attempt for version {version}.", "DBG");
            }
            catch (Exception ex)
            {
                ClientLogger.LogUpdate("Failed to record update attempt.", "Err", ex);
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

                // 1분 이상 지나버린 팝업은 무시
                if (now - scheduledDateTime > TimeSpan.FromMinutes(1))
                    continue;

                string popupKey = scheduledDateTime.ToString("yyyyMMddHHmmss");
                var diff = now - scheduledDateTime;

                // 정각 이후 1분 이내에만 표시 (정각 ~ +1분)
                if (diff >= TimeSpan.Zero &&
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

                // 연장근무 종료 시간 확인 (연장근무가 있으면 연장근무 종료 N분 전에 알림)
                var overtimeEndTime = await GetApprovedOvertimeEndTimeAsync(now);
                if (overtimeEndTime.HasValue)
                {
                    // 연장근무 종료 N분 전을 PC 종료 알림 시각으로 설정
                    var overtimeAlertTime = overtimeEndTime.Value.AddMinutes(-OvertimeAlertMinutesBeforeEnd);

                    // 알림 시각이 변경되었는지 확인 (임계값 이상 차이나면 변경된 것으로 간주)
                    bool alertTimeChanged = !pcOffAlertTargetTime.HasValue ||
                                          Math.Abs((pcOffAlertTargetTime.Value - overtimeAlertTime).TotalSeconds) > AlertTimeChangeThresholdSeconds;

                    if (alertTimeChanged)
                    {
                        pcOffAlertTargetTime = overtimeAlertTime;
                        hasShownPcOffAlert = false;
                    }

                    if (hasShownPcOffAlert)
                        return;

                    if (now >= pcOffAlertTargetTime.Value)
                    {
                        CloseTempDisableTray();
                        hasShownPcOffAlert = true;
                        await ShowPcOffAlertAsync(now, overtimeEndTime.Value, triggeredAfterBoot, isTemporaryDisableActive, isOvertimeAlert: true);
                        isTemporaryDisableActive = false;
                    }
                    return;
                }

                // 종료 예외가 있으면 PC 종료하지 않음
                if (await HasActiveShutdownExceptionAsync(now))
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
                    CloseTempDisableTray();
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

        private async Task<DateTime?> GetApprovedOvertimeEndTimeAsync(DateTime now)
        {
            try
            {
                string today = now.ToString("yyyy-MM-dd");
                var url = $"{ServerBaseUrl}/api/overtime-requests?employeeId={Uri.EscapeDataString(employeeId)}&startDate={today}&endDate={today}";
                using var response = await HttpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                DateTime? currentOvertimeEnd = null;
                DateTime? nextOvertimeEnd = null;
                DateTime? nextOvertimeStart = null;

                foreach (var entry in EnumerateArrayLike(root))
                {
                    string status = GetElementString(entry, "status", "approvalStatus", "approval_status", "result");
                    if (!string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string workDate = GetElementString(entry, "workDate", "work_date", "date");
                    if (!DateTime.TryParse(workDate, out var parsedDate) || parsedDate.Date != now.Date)
                    {
                        continue;
                    }

                    // 연장근무 시작 시간과 종료 시간 파싱
                    string startTime = GetElementString(entry, "startTime", "start_time", "start");
                    string endTime = GetElementString(entry, "endTime", "end_time", "end");

                    if (!TimeSpan.TryParse(startTime, out var startTimeSpan) || !TimeSpan.TryParse(endTime, out var endTimeSpan))
                    {
                        continue;
                    }

                    DateTime startDateTime = now.Date.Add(startTimeSpan);
                    DateTime endDateTime = now.Date.Add(endTimeSpan);

                    // 현재 시각보다 이후의 종료 시간만 고려
                    if (endDateTime <= now)
                    {
                        continue;
                    }

                    // 현재 진행 중인 연장근무인지 확인
                    if (now >= startDateTime && now < endDateTime)
                    {
                        // 진행 중인 연장근무 중 가장 늦게 끝나는 것 선택
                        if (currentOvertimeEnd == null || endDateTime > currentOvertimeEnd.Value)
                        {
                            currentOvertimeEnd = endDateTime;
                        }
                    }
                    // 아직 시작하지 않은 연장근무
                    else if (now < startDateTime)
                    {
                        // 다음 연장근무 중 가장 빨리 시작하는 것 선택
                        if (nextOvertimeStart == null || startDateTime < nextOvertimeStart.Value)
                        {
                            nextOvertimeStart = startDateTime;
                            nextOvertimeEnd = endDateTime;
                        }
                    }
                }

                // 진행 중인 연장근무가 있으면 우선, 없으면 다음 연장근무 반환
                return currentOvertimeEnd ?? nextOvertimeEnd;
            }
            catch
            {
                // 실패 시 null 반환
            }

            return null;
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

        private async Task ShowPcOffAlertAsync(DateTime now, DateTime offTime, bool triggeredAfterBoot, bool isFollowUpAlert, bool isOvertimeAlert = false)
        {
            // 연장근무 알림인 경우: offTime이 종료 시각이므로 그 시각에 PC 종료
            // 일반 업무 종료/일시해제 후속 알림인 경우: 1분 후 PC 종료
            ScheduleShutdown(isOvertimeAlert ? offTime : GetCurrentDateTime().AddMinutes(1));

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
               ? $"연장근무 종료 시각이 임박했습니다. PC가 종료됩니다 (종료 시각: {offTime:HH:mm})."
               : $"업무시간이 종료되어 PC가 종료됩니다 (기준 시각: {offTime:HH:mm}). 일시해제후 진행 업무는 연장근무에 해당되지 않습니다.";

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
                Text = "", // UpdateShutdownCountdownLabel에서 채워짐
                Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Regular)
            };

            string statusText = triggeredAfterBoot || remainingTempDisableCount <= 0
                ? "" // UpdateShutdownCountdownLabel에서 채워짐
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
                StartTempDisableTrayCountdown();

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
            ClientLogger.LogBalloon($"PC off alert shown (target={offTime:HH:mm}, followUp={isFollowUpAlert}, afterBoot={triggeredAfterBoot}).", "DBG");
            UpdateShutdownCountdownLabel();
        }

        private void ScheduleShutdown(DateTime targetTime)
        {
            scheduledShutdownTime = targetTime;
            shutdownCountdownTimer.Start();
            ShowShutdownCountdownTray();
            UpdateShutdownCountdownLabel();
        }

        private void ShutdownCountdownTimer_Tick(object? sender, EventArgs e)
        {
            if (!scheduledShutdownTime.HasValue)
            {
                shutdownCountdownTimer.Stop();
                CloseShutdownCountdownTray();
                return;
            }

            UpdateShutdownCountdownLabel();

            if (GetCurrentDateTime() >= scheduledShutdownTime.Value)
            {
                shutdownCountdownTimer.Stop();
                CloseShutdownCountdownTray();
                ForceShutdown();
            }
        }

        private void UpdateShutdownCountdownLabel()
        {
            if (shutdownCountdownLabel == null || !shutdownCountdownLabel.IsHandleCreated)
            {
                UpdateShutdownCountdownTray();
                return;
            }

            if (!scheduledShutdownTime.HasValue)
            {
                shutdownCountdownLabel!.Text = string.Empty;
                UpdateShutdownCountdownTray();
                return;
            }

            var remaining = scheduledShutdownTime.Value - GetCurrentDateTime();
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            if (pcOffStatusLabel != null && remaining <= TimeSpan.FromMinutes(1) && remainingTempDisableCount <= 0)
            {
                pcOffStatusLabel.Text = $"{(int)remaining.TotalSeconds}초 후 PC가 강제종료됩니다.";
            }

            // 남은 시간을 분과 초로 표시
            if (remaining.TotalMinutes >= 1)
            {
                shutdownCountdownLabel!.Text = $"남은 시간: {remaining.Minutes}분 {remaining.Seconds}초";
            }
            else
            {
                shutdownCountdownLabel!.Text = $"남은 시간: {(int)remaining.TotalSeconds}초";
            }
            UpdateShutdownCountdownTray();
        }

        private void ShowShutdownCountdownTray()
        {
            if (!scheduledShutdownTime.HasValue)
            {
                return;
            }

            if (shutdownCountdownTrayForm == null || shutdownCountdownTrayForm.IsDisposed)
            {
                shutdownCountdownTrayForm = BuildShutdownCountdownTrayForm();
            }

            UpdateShutdownCountdownTray();
            PositionShutdownCountdownTrayForm();
            shutdownCountdownTrayForm.Show();
            shutdownCountdownTrayForm.TopMost = true;
            shutdownCountdownTrayForm.BringToFront();
        }

        private Form BuildShutdownCountdownTrayForm()
        {
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.FixedSingle,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                ControlBox = false,
                TopMost = true,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(260, 80)
            };

            var container = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10)
            };

            container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var titleLabel = new Label
            {
                AutoSize = true,
                Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold),
                Text = "PC 종료까지 남은시간"
            };

            shutdownCountdownTrayLabel = new Label
            {
                AutoSize = true,
                Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold),
                Text = "--:--"
            };

            container.Controls.Add(titleLabel, 0, 0);
            container.Controls.Add(shutdownCountdownTrayLabel, 0, 1);

            form.Controls.Add(container);
            return form;
        }

        private void PositionShutdownCountdownTrayForm()
        {
            if (shutdownCountdownTrayForm == null)
            {
                return;
            }

            const int margin = 10;
            var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
            int x = workingArea.Right - shutdownCountdownTrayForm.Width - margin;
            int y = workingArea.Bottom - shutdownCountdownTrayForm.Height - margin;
            shutdownCountdownTrayForm.Location = new Point(Math.Max(workingArea.Left + margin, x), Math.Max(workingArea.Top + margin, y));
        }

        private void UpdateShutdownCountdownTray()
        {
            if (shutdownCountdownTrayLabel == null)
            {
                return;
            }

            if (!scheduledShutdownTime.HasValue)
            {
                shutdownCountdownTrayLabel.Text = string.Empty;
                return;
            }

            var remaining = scheduledShutdownTime.Value - GetCurrentDateTime();
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            shutdownCountdownTrayLabel.Text = $"{remaining.Minutes:D2}분 {remaining.Seconds:D2}초";
        }

        private void CloseShutdownCountdownTray()
        {
            if (shutdownCountdownTrayForm != null)
            {
                shutdownCountdownTrayForm.Hide();
            }
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
                FormBorderStyle = FormBorderStyle.None,
                ControlBox = false,
                Text = string.Empty,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false,
                ShowIcon = false
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
            ClientLogger.LogBalloon($"Popup displayed (id={popup.Id}, scheduled={popup.ScheduledTime}).", "DBG");
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
                processedIdleIntervalRanges.Clear();
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
                shutdownCountdownTimer.Stop();
                CloseShutdownCountdownTray();
                pcOffKeyDate = currentDate;
                pcOffCountInitializedForDay = false;
                remainingTempDisableCount = 0;
                isTemporaryDisableActive = false;
                CloseTempDisableTray();
            }
        }

        private void StartTempDisableTrayCountdown()
        {
            tempDisableEndTime = pcOffAlertTargetTime;
            if (tempDisableEndTime == null)
            {
                return;
            }

            tempDisableTrayTimer ??= new Timer
            {
                Interval = 1000
            };
            tempDisableTrayTimer.Tick -= TempDisableTrayTimer_Tick;
            tempDisableTrayTimer.Tick += TempDisableTrayTimer_Tick;

            if (tempDisableTrayForm == null || tempDisableTrayForm.IsDisposed)
            {
                tempDisableTrayForm = BuildTempDisableTrayForm();
            }

            UpdateTempDisableTrayTexts();
            PositionTempDisableTrayForm();
            tempDisableTrayForm.Show();
            tempDisableTrayForm.TopMost = true;
            tempDisableTrayForm.BringToFront();
            tempDisableTrayTimer.Start();
        }

        private Form BuildTempDisableTrayForm()
        {
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.FixedSingle,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                ControlBox = false,
                TopMost = true,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(260, 110)
            };

            var container = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };

            container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            container.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            tempDisableUsageLabel = new Label
            {
                AutoSize = true,
                Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold),
                Text = "일시해제 남은시간"
            };

            tempDisableRemainingLabel = new Label
            {
                AutoSize = true,
                Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold),
                Text = "--:--"
            };

            var endButton = new Button
            {
                Text = "일시해제 종료",
                Height = 32,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                AutoSize = true
            };
            endButton.Click += (s, e) => EndTemporaryDisableEarly();

            container.Controls.Add(tempDisableUsageLabel, 0, 0);
            container.Controls.Add(tempDisableRemainingLabel, 0, 1);
            container.Controls.Add(endButton, 0, 2);

            form.Controls.Add(container);
            return form;
        }

        private void PositionTempDisableTrayForm()
        {
            if (tempDisableTrayForm == null)
            {
                return;
            }

            const int margin = 10;
            var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
            int x = workingArea.Right - tempDisableTrayForm.Width - margin;
            int y = workingArea.Bottom - tempDisableTrayForm.Height - margin;
            tempDisableTrayForm.Location = new Point(Math.Max(workingArea.Left + margin, x), Math.Max(workingArea.Top + margin, y));
        }

        private void TempDisableTrayTimer_Tick(object? sender, EventArgs e)
        {
            if (tempDisableEndTime == null)
            {
                CloseTempDisableTray();
                return;
            }

            UpdateTempDisableTrayTexts();

            var remaining = tempDisableEndTime.Value - GetCurrentDateTime();
            if (remaining <= TimeSpan.Zero)
            {
                isTemporaryDisableActive = false;
                CloseTempDisableTray();
                pcOffAlertTargetTime = GetCurrentDateTime();
                _ = TryShowPcOffAlertAsync(triggeredAfterBoot: false);
            }
        }

        private void UpdateTempDisableTrayTexts()
        {
            if (tempDisableUsageLabel == null || tempDisableRemainingLabel == null || tempDisableEndTime == null)
            {
                return;
            }

            int usedCount = Math.Max(0, pcOffSettings.TempDisableCount - remainingTempDisableCount);
            int totalCount = Math.Max(pcOffSettings.TempDisableCount, usedCount);
            tempDisableUsageLabel.Text = $"일시해제 남은시간 (사용횟수 : {usedCount}/{totalCount})";

            var remaining = tempDisableEndTime.Value - GetCurrentDateTime();
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            tempDisableRemainingLabel.Text = $"{remaining.Minutes:D2}분 {remaining.Seconds:D2}초";
        }

        private void EndTemporaryDisableEarly()
        {
            isTemporaryDisableActive = false;
            CloseTempDisableTray();
            pcOffAlertTargetTime = GetCurrentDateTime();
            _ = TryShowPcOffAlertAsync(triggeredAfterBoot: false);
        }

        private void CloseTempDisableTray()
        {
            if (tempDisableTrayTimer != null)
            {
                tempDisableTrayTimer.Stop();
            }

            tempDisableEndTime = null;

            if (tempDisableTrayForm != null)
            {
                tempDisableTrayForm.Hide();
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

        private bool HasOverlappingInterval(DateTime start, DateTime end)
        {
            // 이미 처리된 구간과 겹치는지 확인 (업데이트 후 재시작 시 유사한 구간이 중복으로 표시되는 것을 방지)
            foreach (var range in processedIdleIntervalRanges)
            {
                // 두 구간이 겹치는 경우: (start1 < end2) && (end1 > start2)
                if (start < range.End && end > range.Start)
                {
                    return true;
                }
            }
            return false;
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
            // DPI 스케일링을 무시하고 실제 물리적 화면 크기를 가져옴
            // 이를 통해 100% 배율 기준으로 팝업 이미지 크기를 고정
            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);

            const int padding = 24; // 화면 가득 차지하지 않도록 최소 여백 확보

            int maxWidth = Math.Max(PopupImageMinWidth, screenWidth - padding);
            int maxHeight = Math.Max(PopupImageMinHeight, screenHeight - padding); ;

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

                // GetLastInputTime()을 한 번만 호출하여 시스템 호출 최소화
                DateTime currentInputTime = GetLastInputTime();
                bool inputDetected = (currentInputTime - lastInputTime).TotalSeconds > 1;

                bool isLunchBreak = IsLunchBreak(nowTime);
                if (wasInLunchBreak && !isLunchBreak)
                {
                    // 점심시간이 끝났을 때, 사용자가 실제로 자리에 있는 경우에만 idle 상태를 리셋
                    // 자리에 없는 경우(idle 상태)는 유지하여, 점심시간을 포함한 자리비움 구간이 제대로 감지되도록 함
                    if (inputDetected)
                    {
                        lastInputTime = currentInputTime;
                        isIdle = false;
                        hasShownPopup = false;
                        idleStartedDuringWork = false;
                    }
                    // idle 상태인 경우는 리셋하지 않고 유지하여 점심 이후 복귀 시 팝업이 뜨도록 함
                }
                wasInLunchBreak = isLunchBreak;

                bool isWorkingTime = IsWorkingTime(nowTime);                

                bool isAfterWork = nowTime > workEndTime;
                
                // 1) 근무시간: 기존 팝업 방식
                if (!isAfterWork && isWorkingTime)
                {
                    if (inputDetected)
                    {
                        if (isIdle && !hasShownPopup)
                        {
                            hasShownPopup = true;
                            DateTime idleEndTime = currentInputTime;
                            ClientLogger.LogAgent($"Idle detected during work hours {idleStartTime:HH:mm:ss}-{idleEndTime:HH:mm:ss}.", "DBG");
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
                            ClientLogger.LogAgent($"User became idle at {idleStartTime:HH:mm:ss} (threshold: {idleThreshold.TotalMinutes} min).", "DBG");
                        }
                    }

                    return;
                }

                // 2) 근무시간도 아니고 퇴근 후도 아닌 구간(점심 등): 무시
                if (!isAfterWork && !isWorkingTime)
                {
                    // 점심시간 중에 입력이 감지된 경우에만 lastInputTime 업데이트
                    // 입력이 없는 경우(idle 상태)는 이전 lastInputTime 유지
                    if (inputDetected)
                    {
                        lastInputTime = currentInputTime;
                    }
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
                ClientLogger.LogService($"Power mode changed to SUSPEND at {suspendStartTime:HH:mm:ss}.", "DBG");
                await SendPcEventAsync("SUSPEND");
            }
            else if (e.Mode == PowerModes.Resume)
            {
                // 절전에서 다시 깨어날 때
                DateTime resumeTime = GetCurrentDateTime();
                ClientLogger.LogService($"Power mode changed to RESUME at {resumeTime:HH:mm:ss}.", "DBG");
                await SendPcEventAsync("RESUME");

                // suspendStartTime이 유효한 값인지 확인 (프로그램 시작 후 첫 resume 이벤트 방지)
                if (suspendStartTime == DateTime.MinValue)
                {
                    // 실제 마지막 입력 시간으로 설정 (절전 전 입력 시간을 보존)
                    lastInputTime = GetLastInputTime();
                    RestoreNotifyIcon();
                    return;
                }

                var suspendTimeOfDay = suspendStartTime.TimeOfDay;
                var resumeTimeOfDay = resumeTime.TimeOfDay;
                var suspendDuration = resumeTime - suspendStartTime;

                bool suspendDuringWork = IsWorkingTime(suspendTimeOfDay);
                bool resumeDuringWork = IsWorkingTime(resumeTimeOfDay);

                if (suspendDuringWork || resumeDuringWork)
                {
                    // 절전/복귀 중 하나라도 근무시간이면 자리비움으로 처리
                    // 실제 idle 시작 시간은 lastInputTime (절전 시작보다 이전일 수 있음)
                    // 예: 10:00에 마지막 입력, 10:05에 절전, 10:15에 복귀
                    //     → 실제 idle은 10:00-10:15 (15분)
                    DateTime actualIdleStart = lastInputTime < suspendStartTime ? lastInputTime : suspendStartTime;

                    ClientLogger.LogAgent($"Idle interval detected during suspend/resume. Last input: {lastInputTime:HH:mm:ss}, Suspend: {suspendStartTime:HH:mm:ss}, Resume: {resumeTime:HH:mm:ss}, Duration: {suspendDuration.TotalMinutes:F1} min, Actual idle start: {actualIdleStart:HH:mm:ss}.", "DBG");
                    await HandleIdleIntervalAsync(actualIdleStart, resumeTime);
                }

                // 절전 복귀 후 실제 마지막 입력 시간으로 갱신
                // 절전 전 사용자의 실제 마지막 활동 시간을 추정하여 보존
                // (절전 직전에 사용자가 활동했다면 suspendStartTime을, 아니면 이전 lastInputTime 유지)
                DateTime estimatedLastInput = GetLastInputTime();

                // GetLastInputTime()이 현재 시각에 가까운 값을 반환하는 경우
                // (절전 복귀 직후 시스템이 초기화되면서 발생 가능)
                // 이 경우 suspendStartTime 이전의 lastInputTime을 보존해야 함
                if (Math.Abs((resumeTime - estimatedLastInput).TotalSeconds) < ResumeInputDetectionThresholdSeconds)
                {
                    // 절전 복귀 직후이므로 실제 사용자 입력이 아님
                    // lastInputTime을 절전 전 값 또는 suspendStartTime 이전 값으로 유지
                    if (lastInputTime > suspendStartTime)
                    {
                        // lastInputTime이 절전 시작보다 나중이면 이상한 상태
                        // suspendStartTime보다 약간 이전으로 보정
                        ClientLogger.LogService($"ANOMALY DETECTED: lastInputTime ({lastInputTime:HH:mm:ss}) > suspendStartTime ({suspendStartTime:HH:mm:ss}). Adjusting to {suspendStartTime.AddSeconds(-LastInputTimeAdjustmentOffsetSeconds):HH:mm:ss}.", "WARN");
                        lastInputTime = suspendStartTime.AddSeconds(-LastInputTimeAdjustmentOffsetSeconds);
                    }
                    // 그렇지 않으면 기존 lastInputTime 유지 (절전 전 값)
                    ClientLogger.LogService($"Preserving lastInputTime from before suspend: {lastInputTime:HH:mm:ss}", "DBG");
                }
                else
                {
                    // 실제 사용자 입력으로 보임 (임계값 이상 차이)
                    lastInputTime = estimatedLastInput;
                    ClientLogger.LogService($"Updated lastInputTime to actual input time: {lastInputTime:HH:mm:ss}", "DBG");
                }
                           
                RestoreNotifyIcon();
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
                ClientLogger.LogService($"Session locked at {sessionLockStartTime:HH:mm:ss}.", "DBG");
                ClientLogger.LogAgent($"Session lock detected, sessionLockStartTime set to {sessionLockStartTime:HH:mm:ss}.", "DBG");
                await SendPcEventAsync("SESSION_LOCK");
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                // 화면 잠금 해제
                DateTime unlockTime = GetCurrentDateTime();
                ClientLogger.LogService($"Session unlocked at {unlockTime:HH:mm:ss}.", "DBG");
                ClientLogger.LogAgent($"Session unlock detected, unlockTime={unlockTime:HH:mm:ss}, sessionLockStartTime={sessionLockStartTime:HH:mm:ss}.", "DBG");
                await SendPcEventAsync("SESSION_UNLOCK");

                // sessionLockStartTime이 유효한 값인지 확인 (프로그램 시작 후 첫 unlock 이벤트 방지)
                if (sessionLockStartTime == DateTime.MinValue)
                {
                    ClientLogger.LogAgent($"sessionLockStartTime is MinValue, skipping idle interval processing.", "DBG");
                    ResetIdleState(unlockTime);
                    return;
                }

                var lockTimeOfDay = sessionLockStartTime.TimeOfDay;
                var unlockTimeOfDay = unlockTime.TimeOfDay;

                bool lockDuringWork = IsWorkingTime(lockTimeOfDay);
                bool unlockDuringWork = IsWorkingTime(unlockTimeOfDay);

                var lockDuration = unlockTime - sessionLockStartTime;
                ClientLogger.LogAgent($"Lock time: {sessionLockStartTime:HH:mm:ss}, Unlock time: {unlockTime:HH:mm:ss}, Duration: {lockDuration.TotalMinutes:F1} min, Lock during work: {lockDuringWork}, Unlock during work: {unlockDuringWork}.", "DBG");

                if (lockDuringWork || unlockDuringWork)
                {
                    // 잠금/해제 중 하나라도 근무시간이면 자리비움으로 처리
                    // 실제 idle 시작 시간은 lastInputTime (화면 잠금보다 이전일 수 있음)
                    // 예: 10:00에 마지막 입력, 10:05에 화면 잠금 (자리 비운 후 나중에 잠금), 10:15에 해제
                    //     → 실제 idle은 10:00-10:15 (15분)
                    DateTime actualIdleStart = lastInputTime < sessionLockStartTime ? lastInputTime : sessionLockStartTime;

                    ClientLogger.LogAgent($"Idle interval detected during session lock/unlock. Last input: {lastInputTime:HH:mm:ss}, Lock: {sessionLockStartTime:HH:mm:ss}, Unlock: {unlockTime:HH:mm:ss}, Actual idle start: {actualIdleStart:HH:mm:ss}.", "DBG");
                    await HandleIdleIntervalAsync(actualIdleStart, unlockTime);
                }
                else
                {
                    ClientLogger.LogAgent($"No work time overlap for session lock/unlock, skipping idle interval.", "DBG");
                }

                // 세션 잠금 해제 시에는 사용자가 비밀번호를 입력했으므로 실제 입력이 있음
                // 따라서 unlockTime 또는 실제 입력 시간으로 갱신
                DateTime actualInputTime = GetLastInputTime();

                // 실제 입력이 unlock 시간과 가까우면 (임계값 이내) 실제 입력 시간 사용
                if (Math.Abs((unlockTime - actualInputTime).TotalSeconds) < ResumeInputDetectionThresholdSeconds)
                {
                    lastInputTime = actualInputTime;
                    ClientLogger.LogService($"Updated lastInputTime to actual input after unlock: {lastInputTime:HH:mm:ss}", "DBG");
                }
                else
                {
                    // 임계값 이상 차이나면 unlock 시간을 사용 (보수적 접근)
                    lastInputTime = unlockTime;
                    ClientLogger.LogService($"Updated lastInputTime to unlock time: {lastInputTime:HH:mm:ss}", "DBG");
                }

                isIdle = false;
                hasShownPopup = false;
                RestoreNotifyIcon();
            }
        }

        // 자리비움 상태를 리셋하고 마지막 입력 시간을 업데이트
        private void ResetIdleState(DateTime currentTime)
        {
            lastInputTime = currentTime;
            isIdle = false;
            hasShownPopup = false;
        }

        // 세션이 현재 잠겨있는지 확인
        private bool IsSessionLocked()
        {
            try
            {
                const int DESKTOP_READOBJECTS = 0x0001;
                IntPtr hDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
                if (hDesktop == IntPtr.Zero)
                {
                    // OpenInputDesktop이 실패하면 데스크톱이 잠겨있음
                    return true;
                }
                CloseDesktop(hDesktop);
                return false;
            }
            catch
            {
                // 오류 발생 시 안전하게 잠기지 않은 것으로 간주
                return false;
            }
        }

        // -----------------------------
        // Windows 시스템 종료/로그오프 처리
        // -----------------------------
        private void SystemEvents_SessionEnding(object? sender, SessionEndingEventArgs e)
        {
            try
            {
                ClientLogger.LogService($"Windows session ending detected: {e.Reason}", "DBG");

                // PC_OFF는 실제 시스템 종료(Shutdown) 시에만 전송
                // 로그오프(Logoff)는 제외
                if (e.Reason == SessionEndReasons.SystemShutdown)
                {
                    isSystemShuttingDown = true;
                    // 동기적으로 PC_OFF 이벤트 전송 (비동기로 하면 프로세스 종료되어 전송 실패 가능)
                    SendPcEventSync("PC_OFF");
                    ClientLogger.LogService("PC_OFF event sent for system shutdown.", "DBG");
                }
                else
                {
                    ClientLogger.LogService($"Session ending reason is {e.Reason}, not sending PC_OFF.", "DBG");
                }
            }
            catch (Exception ex)
            {
                ClientLogger.LogService($"Error in SessionEnding handler: {ex.Message}", "Err");
            }
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
            // 동시 실행 방지: 여러 이벤트(idle 타이머, 절전 복귀, 세션 잠금 해제)가 동시에 발생할 수 있으므로
            // 세마포어로 순차 처리하여 중복 팝업을 방지
            ClientLogger.LogAgent($"HandleIdleIntervalAsync called for {start:HH:mm:ss}-{end:HH:mm:ss}.", "DBG");

            await idleIntervalSemaphore.WaitAsync();

            try
            {
                ClearStaleIdleKeys();
                var segments = SplitIdleInterval(start, end);
                ClientLogger.LogAgent($"Split idle interval into {segments.Count} segment(s).", "DBG");

                foreach (var segment in segments)
                {
                    // 겹치는 구간이 이미 처리되었는지 확인 (업데이트 재시작 시 유사한 구간 중복 방지)
                    if (HasOverlappingInterval(segment.Start, segment.End))
                    {
                        ClientLogger.LogAgent($"Skipping overlapping interval {segment.Start:HH:mm:ss}-{segment.End:HH:mm:ss}.", "DBG");
                        continue;
                    }

                    string intervalKey = GetIdleIntervalKey(segment.Start, segment.End);
                    if (processedIdleIntervals.Contains(intervalKey))
                    {
                        ClientLogger.LogAgent($"Skipping already processed interval {intervalKey}.", "DBG");
                        continue;
                    }

                    processedIdleIntervals.Add(intervalKey);
                    processedIdleIntervalRanges.Add((segment.Start, segment.End));
                    ClientLogger.LogAgent($"Processing idle interval {segment.Start:HH:mm:ss}-{segment.End:HH:mm:ss}.", "DBG");
                    await ShowIdleReasonPopupAsync(segment.Start, segment.End);
                }
            }
            catch (Exception ex)
            {
                ClientLogger.LogAgent($"Error in HandleIdleIntervalAsync: {ex.Message}", "Err", ex);
            }
            finally
            {
                idleIntervalSemaphore.Release();
                ClientLogger.LogAgent($"HandleIdleIntervalAsync completed.", "DBG");
            }           
        }

        private List<(DateTime Start, DateTime End)> SplitIdleInterval(DateTime start, DateTime end)
        {
            var segments = new List<(DateTime Start, DateTime End)>();

            if (end <= start)
            {
                return segments;
            }

            // 날짜가 넘어가는 자리비움 체크: 퇴근 후 복귀하지 않은 경우 자리비움으로 기록하지 않음
            // 예: 2025-12-15 16:29에 외근 후 복귀하지 않고, 2025-12-16 09:17에 컴퓨터를 켰다면
            // 이는 퇴근한 것이므로 자리비움 사유를 묻지 않음
            if (start.Date != end.Date)
            {
                DateTime workDayEnd = start.Date.Add(workEndTime);

                // 시작일 업무종료 시각 이후에도 복귀하지 않았다면 (즉, 퇴근한 것으로 간주)
                // 자리비움으로 기록하지 않음
                if (end > workDayEnd)
                {
                    ClientLogger.LogAgent($"Skipping multi-day idle interval {start:yyyy-MM-dd HH:mm:ss}-{end:yyyy-MM-dd HH:mm:ss} (did not return before work end on {start:yyyy-MM-dd}).", "DBG");
                    return segments;
                }
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
            // 모달 다이얼로그 표시 전 heartbeat 강제 갱신
            // Watcher가 120초 타임아웃으로 앱을 재시작하는 것을 방지
            heartbeatWriter?.ForceUpdate();

            try
            {
                ClientLogger.LogAgent($"Showing idle reason popup for {start:HH:mm:ss}-{end:HH:mm:ss}.", "DBG");
                using IdleReasonForm form = new IdleReasonForm(start, end, ServerBaseUrl);
                var result = form.ShowDialog();

                if (result == DialogResult.OK)
                {
                    ClientLogger.LogAgent($"User confirmed idle reason form.", "DBG");
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

                    ClientLogger.LogAgent($"Idle reason captured ({idleEvent.ReasonDetail}) {start:HH:mm}-{end:HH:mm}.", "DBG");
                    bool success = await SendIdleEventAsync(idleEvent);
                    if (!success)
                    {
                        SavePendingIdleEvent(idleEvent);
                        ClientLogger.LogAgent($"Idle event saved locally due to server failure. Showing notification to user.", "Err");
                        MessageBox.Show("서버 전송에 실패했습니다. 데이터를 로컬에 저장했습니다.");
                    }
                }
                else
                {
                    ClientLogger.LogAgent($"User cancelled idle reason form.", "DBG");
                }
            }
            catch (Exception ex)
            {
                ClientLogger.LogAgent($"Error showing idle reason popup: {ex.Message}", "Err", ex);
                // Don't re-throw to prevent application crash
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
            ClientLogger.LogAgent($"After-hours idle auto submission {start:HH:mm}-{end:HH:mm} recorded.", "DBG");
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
                Installed = 1,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private record struct ClientStatusResult(bool Success, bool UserNotFound);

        private async Task<ClientStatusResult> PostClientStatusAsync(string url)
        {
            var payload = BuildClientStatusPayload();
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var endpoint = TryGetEndpoint(url);

            try
            {
                ClientLogger.LogWeb($"Posting client status to {endpoint} for {payload.EmpNo}/{payload.PcName}.", "DBG");

                var response = await HttpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    LogClientStatusIssue(
                        $"[{payload.EmpNo}/{payload.PcName}] {url} 응답 오류",
                        response: response,
                        body: responseBody);
                    return new ClientStatusResult(false, response.StatusCode == HttpStatusCode.NotFound);
                }
                else
                {
                    ClientLogger.LogWeb($"[{payload.EmpNo}] POST {endpoint} success.", "DBG");
                    return new ClientStatusResult(true, false);
                }
            }
            catch (Exception ex)
            {
                LogClientStatusIssue(
                    $"[{payload.EmpNo}/{payload.PcName}] {url} 요청 예외",
                    ex);
                return new ClientStatusResult(false, false);
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

                var logMessage = response != null
                    ? $"{context} (Status {(int)response.StatusCode})"
                    : context;
                ClientLogger.LogWeb(logMessage, "Err", ex);
            }
            catch
            {
                // 로깅 실패는 무시
            }
        }

        private void ScheduleUserInfoReentryPrompt()
        {
            lock (userInfoPromptLock)
            {
                if (userInfoPromptScheduled)
                {
                    return;
                }

                CleanupUserInfoRetryTimerLocked();

                userInfoPromptScheduled = true;
                userInfoRetryTimer = new Timer
                {
                    Interval = UserInfoRetryDelayMs
                };

                userInfoRetryTimer.Tick += UserInfoRetryTimer_Tick;

                userInfoRetryTimer.Start();
            }
        }

        private void UserInfoRetryTimer_Tick(object? sender, EventArgs e)
        {
            lock (userInfoPromptLock)
            {
                CleanupUserInfoRetryTimerLocked();
                userInfoPromptScheduled = false;
            }

            MessageBox.Show(
                "서버에서 사용자 정보를 찾을 수 없습니다. 이름과 사번을 다시 입력해주세요.",
                "사용자 정보 필요",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            OnEditUserInfo(this, EventArgs.Empty);
        }

        private void CleanupUserInfoRetryTimer()
        {
            lock (userInfoPromptLock)
            {
                CleanupUserInfoRetryTimerLocked();
            }
        }

        private void CleanupUserInfoRetryTimerLocked()
        {
            if (userInfoRetryTimer == null)
            {
                return;
            }

            userInfoRetryTimer.Stop();
            userInfoRetryTimer.Tick -= UserInfoRetryTimer_Tick;
            userInfoRetryTimer.Dispose();
            userInfoRetryTimer = null;
        }

        private static string TryGetEndpoint(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        }

        private async Task<bool> SendIdleEventAsync(IdleEventData data)
        {
            try
            {
                var client = HttpClient;
                string url = $"{ServerBaseUrl}/api/idle-events";
                string json = JsonSerializer.Serialize(data);

                ClientLogger.LogWeb($"Sending idle event for {data.EmployeeId} ({data.IdleStartTime} ~ {data.IdleEndTime}, reason: {data.ReasonDetail}).", "DBG");
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    RemovePendingIdleEvent(data.Id);
                    ClientLogger.LogWeb($"Idle event sent successfully ({data.EmployeeId}, {data.Id}).", "DBG");
                    return true;
                }
                else
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    ClientLogger.LogWeb($"Idle event send failed ({data.EmployeeId}) Status {(int)response.StatusCode}, Body: {responseBody}.", "Err");
                    SavePendingIdleEvent(data);
                    return false;
                }
            }
            catch (Exception ex)
            {
                ClientLogger.LogWeb($"Idle event send exception ({data.EmployeeId}, {data.Id}).", "Err", ex);
                SavePendingIdleEvent(data);
                return false;
            }
        }

        private async Task SendPcEventAsync(string eventType)
        {
            try
            {
                var client = HttpClient;
                string url = $"{ServerBaseUrl}/api/pc-events";

                DateTime eventTime = eventType == "PC_ON"
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

                ClientLogger.LogWeb($"Sending PC event {eventType} for {employeeId} at {eventTime:HH:mm:ss}.", "DBG");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    ClientLogger.LogWeb($"PC event {eventType} sent successfully for {employeeId}.", "DBG");
                }
                else
                {
                    ClientLogger.LogWeb($"PC event {eventType} send failed with status {(int)response.StatusCode} for {employeeId}.", "Err");
                }
            }
            catch (Exception ex)
            {
                ClientLogger.LogWeb($"Failed to send PC event {eventType} for {employeeId}.", "Err", ex);
                // 실패해도 별도 저장은 하지 않고 무시
            }
        }

        // 동기 버전: Windows 종료 시 사용 (비동기로 하면 프로세스가 종료되어 전송 실패 가능)
        private void SendPcEventSync(string eventType)
        {
            try
            {
                var client = HttpClient;
                string url = $"{ServerBaseUrl}/api/pc-events";

                DateTime eventTime = eventType == "PC_ON"
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

                ClientLogger.LogWeb($"Sending PC event {eventType} (sync) for {employeeId} at {eventTime:HH:mm:ss}.", "DBG");

                // 동기 호출: Windows 종료 중에는 비동기 메서드를 기다릴 수 없으므로 동기적으로 실행
                // ConfigureAwait(false)를 사용하여 컨텍스트 캡처를 피하고 데드락 방지
                var response = client.PostAsync(url, content).ConfigureAwait(false).GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    ClientLogger.LogWeb($"PC event {eventType} sent successfully for {employeeId}.", "DBG");
                }
                else
                {
                    ClientLogger.LogWeb($"PC event {eventType} send failed with status {(int)response.StatusCode} for {employeeId}.", "Err");
                }
            }
            catch (Exception ex)
            {
                ClientLogger.LogWeb($"Failed to send PC event {eventType} for {employeeId}.", "Err", ex);
                // Windows 종료 중에는 네트워크 연결이 이미 끊어지거나 시간 제약이 있을 수 있으므로
                // 실패 시 별도 저장이나 재시도 없이 무시 (시스템이 곧 종료됨)
            }
        }

        private static string GetLocalIPAddress()
        {
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    var properties = networkInterface.GetIPProperties();
                    foreach (var address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(address.Address))
                        {
                            return address.Address.ToString();
                        }
                    }
                }
            }
            catch
            {
                // ignore and fall through
            }

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ip != null)
                {
                    return ip.ToString();
                }
            }
            catch
            {
                // ignore
            }

            return "127.0.0.1";
        }

#if DEBUG
        private void Form1_DebugKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11)
            {
                var now = GetCurrentDateTime();
                MessageBox.Show(this, $"현재 기준 시각: {now:yyyy-MM-dd HH:mm:ss}", "디버그 시간");
                e.Handled = true;
            }
        }
#endif

        private async Task SendHeartbeatAsync()
        {
            // Use semaphore to prevent race conditions
            if (!await heartbeatSemaphore.WaitAsync(0))
            {
                return;
            }
                       
            try
            {
                // /api/client/heartbeat로 변경
                var result = await PostClientStatusAsync($"{ServerBaseUrl}/api/client/heartbeat");
                if (result.UserNotFound)
                {
                    ScheduleUserInfoReentryPrompt();
                }
            }
            catch
            {
                // 하트비트 실패 시는 무시 (주기적으로 재시도됨)
            }
            finally
            {
                heartbeatSemaphore.Release();
            }
        }

        private async Task CheckEmployeeOvertimeStatusAsync()
        {
            if (isCheckingEmployeeOvertimeStatus)
            {
                return;
            }

            isCheckingEmployeeOvertimeStatus = true;
            try
            {
                // 간단한 폴링 자리만 유지. 추후 확장 시 lastKnownOvertimeStatuses 활용 가능.
                await Task.CompletedTask;
            }
            finally
            {
                isCheckingEmployeeOvertimeStatus = false;
            }
        }

        // -----------------------------
        // URL 모니터링 및 금지 사이트 확인
        // -----------------------------
        private void CheckBrowserUrl()
        {
            try
            {
                // 금지 URL 목록이 없으면 체크하지 않음
                if (prohibitedUrls == null || prohibitedUrls.Count == 0)
                    return;

                // 현재 활성화된 브라우저의 URL 가져오기
                string? currentUrl = BrowserUrlMonitor.GetCurrentBrowserUrl();

                if (string.IsNullOrWhiteSpace(currentUrl))
                {                   
                    return;
                }

                // URL이 변경되었는지 확인 (도메인 접속 시 즉시 감지)
                bool urlChanged = previousUrl != currentUrl;
                previousUrl = currentUrl;

                // URL이 변경되었고 금지된 URL인 경우에만 알림 표시 (반복 알림 없음)
                if (urlChanged && BrowserUrlMonitor.IsProhibitedUrl(currentUrl, prohibitedUrls))
                {
                    string normalizedUrl = NormalizeUrlForDisplay(currentUrl);
                    if (IsAlertSuppressed(normalizedUrl))
                    {
                        return;
                    }

                    lastAlertedUrl = normalizedUrl;
                    lastAlertedAt = DateTime.Now;
                    ShowProhibitedUrlAlert(currentUrl);

                    // 로그 기록 (전체 URL 경로 포함)
                    ClientLogger.LogAgent($"Prohibited URL accessed: {currentUrl}", "WARN");
                }
            }
            catch (Exception ex)
            {
                // URL 체크 실패는 조용히 무시 (중요하지 않은 기능)
                ClientLogger.LogAgent($"URL monitoring error: {ex.Message}", "DBG");
            }
        }

        private void ShowProhibitedUrlAlert(string url)
        {
            try
            {
                string title = "영업금지 사이트 알림";
                string fullUrl = NormalizeUrlForDisplay(url);

                // 트레이 아이콘 풍선 알림 표시
                notifyIcon.BalloonTipTitle = title;
                notifyIcon.BalloonTipText = $"영업금지 URL: {fullUrl}";
                notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                notifyIcon.ShowBalloonTip(5000);

                string companyName = ResolveCompanyName(url);
                IntPtr browserWindowHandle = BrowserUrlMonitor.GetForegroundBrowserWindowHandle();
                using var alertForm = new ProhibitedUrlAlertForm(companyName, fullUrl, browserWindowHandle);
                alertForm.ShowDialog(this);
            }
            catch (Exception ex)
            {
                ClientLogger.LogAgent($"Failed to show prohibited URL alert: {ex.Message}", "Err");
            }
        }

        private string ResolveCompanyName(string url)
        {
            if (prohibitedUrlRows == null || prohibitedUrlRows.Count == 0)
            {
                return string.Empty;
            }

            foreach (var row in prohibitedUrlRows)
            {
                if (string.IsNullOrWhiteSpace(row.Url))
                {
                    continue;
                }

                if (BrowserUrlMonitor.IsProhibitedUrl(url, new List<string> { row.Url }))
                {
                    return row.CompanyName ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private bool IsAlertSuppressed(string normalizedUrl)
        {
            if (string.IsNullOrWhiteSpace(normalizedUrl))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(lastAlertedUrl))
            {
                return false;
            }

            if (!string.Equals(lastAlertedUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return DateTime.Now - lastAlertedAt < prohibitedAlertCooldown;
        }

        private static string NormalizeUrlForDisplay(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return uri.ToString();
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string withScheme = $"https://{url}";
                if (Uri.TryCreate(withScheme, UriKind.Absolute, out uri))
                {
                    return uri.ToString();
                }
            }

            return url;
        }

        private async Task ResendPendingIdleEventsAsync()
        {
            if (!File.Exists(pendingIdleEventsFile))
            {
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(pendingIdleEventsFile, Encoding.UTF8);
                var pending = JsonSerializer.Deserialize<List<IdleEventData>>(json, JsonOptions) ?? new List<IdleEventData>();
                if (pending.Count == 0)
                {
                    File.Delete(pendingIdleEventsFile);
                    return;
                }

                var remaining = new List<IdleEventData>();
                foreach (var idleEvent in pending)
                {
                    bool success = await SendIdleEventAsync(idleEvent);
                    if (!success)
                    {
                        remaining.Add(idleEvent);
                    }
                }

                if (remaining.Count == 0)
                {
                    File.Delete(pendingIdleEventsFile);
                }
                else
                {
                    var updatedJson = JsonSerializer.Serialize(remaining, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(pendingIdleEventsFile, updatedJson, Encoding.UTF8);
                }
            }
            catch
            {
                // 재전송 실패는 무시하고 다음 주기에 재시도
            }
        }

        private async Task RegisterOrUpdateClientAsync()
        {
            try
            {
                var result = await PostClientStatusAsync($"{ServerBaseUrl}/api/client/register");
                if (result.UserNotFound)
                {
                    ScheduleUserInfoReentryPrompt();
                }
            }
            catch
            {
                // 연결 장애 시 등록 실패는 조용히 무시하고 재시도
            }
        }

        private async Task CheckAndAddManagerMenuAsync()
        {
            try
            {
                var emp = (employeeId ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(emp))
                    return;

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
                // ignore
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
                // 파싱 실패 시 빈 lista 반환
            }

            return list;
        }

        private void ShowManagerNotificationAlert(List<ManagerNotificationItem> newNotifications)
        {
            if (newNotifications.Count == 0)
                return;

            lastBalloonKind = BalloonNotificationKind.Manager;
            lastBalloonShownAt = DateTime.Now;
            notifyIcon.BalloonTipTitle = "연장근무승인 결재";
            notifyIcon.BalloonTipText = $"새 연장 근무 신청 {newNotifications.Count}건이 도착했습니다. 클릭하여 확인하세요.";
            notifyIcon.ShowBalloonTip(4000);
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
                // 이미 열려있는 창이 있으면 앞으로 가져오기
                if (managerNotificationListForm != null && !managerNotificationListForm.IsDisposed)
                {
                    managerNotificationListForm.BringToFront();
                    managerNotificationListForm.Activate();
                    return;
                }

                // 알림 목록을 열 때 알림 타이머 리셋 (확인하지 않은 것으로 간주)
                lastManagerNotificationAlertTime = DateTime.MinValue;

                managerNotificationListForm = new ManagerNotificationListForm(ServerBaseUrl, HttpClient, employeeId, employeeName, notificationIdsToMark);
                managerNotificationListForm.Owner = this;
                managerNotificationListForm.FormClosed += (s, e) => managerNotificationListForm = null;
                managerNotificationListForm.Show();
            }
            catch (Exception ex)
            {
#if DEBUG
                MessageBox.Show($"연장 근무 알림을 여는 중 오류가 발생했습니다.\n{ex.Message}");
#endif
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

            lastBalloonKind = BalloonNotificationKind.General;
            lastBalloonShownAt = DateTime.Now;
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
                return JsonSerializer.Deserialize<List<IdleEventData>>(json, JsonOptions) ?? new List<IdleEventData>();
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
                MessageBox.Show("연장 근무 신청은 업무시간(17:30) 이전에만 가능합니다.");
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

        private async void OnDebugRunUpdateTest(object? sender, EventArgs e)
        {
#if DEBUG
            await CheckClientUpdateAsync(forceInstall: true, notifyWhenNoUpdate: true);
#endif
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

        private void InitializeTrayMenu()
        {
            trayMenu?.Dispose();

            trayMenu = new ContextMenuStrip();
            // 메인 폼을 열면 닫을 때 애플리케이션이 종료되므로 해당 메뉴 제거
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
            trayMenu.Items.Add("디버그: 업데이트 테스트 실행", null, OnDebugRunUpdateTest);
            trayMenu.Items.Add("디버그: 관리자 진단", null, async (s, e) => await CheckAndAddManagerMenuAsync());
            trayMenu.Items.Add("디버그: 연장근무 관리자 알림 확인", null, async (s, e) => await CheckManagerNotificationsAsync(forceShowPopup: true));
            trayMenu.Items.Add("디버그: 연장근무 직원 알림 확인", null, async (s, e) => await CheckEmployeeOvertimeStatusAsync());
#endif

            if (notifyIcon != null)
            {
                notifyIcon.ContextMenuStrip = trayMenu;
                notifyIcon.Visible = true;
            }

            // 관리자 여부 비동기 확인: 관리자라면 관리용 메뉴 추가
            _ = CheckAndAddManagerMenuAsync();
        }

        private void RestoreNotifyIcon()
        {
            if (notifyIcon == null)
            {
                return;
            }

            try
            {
                if (trayMenu == null || trayMenu.IsDisposed)
                {
                    InitializeTrayMenu();
                }

                var resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
                notifyIcon.Icon = (Icon?)resources.GetObject("notifyIcon.Icon") ?? notifyIcon.Icon;
                notifyIcon.Text = ApplicationName;
                notifyIcon.Visible = false;
                notifyIcon.Visible = true;
            }
            catch
            {
                // 트레이 아이콘 복구 실패는 무시
            }
        }

        private Task OnManagerNotificationBalloonClickedAsync()
        {
            try
            {
                if (!isManagerUser && !IsDebugBuild())
                {
                    return Task.CompletedTask;
                }

                if (lastBalloonKind != BalloonNotificationKind.Manager)
                {
                    return Task.CompletedTask;
                }

                if (DateTime.Now - lastBalloonShownAt > BalloonClickWindow)
                {
                    return Task.CompletedTask;
                }

                lastBalloonKind = BalloonNotificationKind.None;

                // 이미 열려있는 창이 있으면 앞으로 가져오기
                if (managerNotificationListForm != null && !managerNotificationListForm.IsDisposed)
                {
                    managerNotificationListForm.BringToFront();
                    managerNotificationListForm.Activate();
                    return Task.CompletedTask;
                }

                return OpenManagerNotificationsAsync(lastAlertedManagerNotificationIds);
            }
            catch
            {
                // 폼 생성 실패 시 무시
            }

            return Task.CompletedTask;
        }

        private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                // 업데이트 중인 경우 LOGOUT 이벤트 전송을 건너뜀
                // 이유: 빠른 종료를 위해 서버 통신을 생략하고 즉시 프로세스를 종료해야 인스톨러가 파일을 교체할 수 있음
                if (isUpdatingClient)
                {
                    ClientLogger.LogUpdate("Application closing for update. Skipping LOGOUT event to ensure quick termination.", "DBG");
                    return;
                }

                // Windows 시스템 종료 중이면 PC_OFF는 이미 전송했으므로 LOGOUT만 전송
                if (isSystemShuttingDown)
                {
                    ClientLogger.LogService("Application closing due to system shutdown, sending LOGOUT only.", "DBG");
                    await SendPcEventAsync("LOGOUT").ConfigureAwait(false);
                }
                else
                {
                    // 일반적인 프로그램 종료 (사용자가 수동으로 종료하거나 로그아웃)
                    // PC_OFF는 전송하지 않고 LOGOUT만 전송
                    ClientLogger.LogService("Application closing (manual/logout), sending LOGOUT only.", "DBG");
                    await SendPcEventAsync("LOGOUT").ConfigureAwait(false);
                }
            }
            catch
            {
                // 종료 시 이벤트 전송 실패는 무시
            }
            finally
            {
                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }

                trayMenu?.Dispose();
            }
        }

        private void SavePendingIdleEvent(IdleEventData data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pendingIdleEventsFile)!);
                var events = LoadPendingIdleEvents();

                var existingIndex = events.FindIndex(e => string.Equals(e.Id, data.Id, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    events[existingIndex] = data;
                }
                else
                {
                    events.Add(data);
                }

                var json = JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(pendingIdleEventsFile, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 로컬 저장 실패 시 로그 기록
                ClientLogger.LogAgent($"Failed to save pending idle event: {ex.Message}", "Err", ex);
            }
        }

        private void RemovePendingIdleEvent(string id)
        {
            try
            {
                if (!File.Exists(pendingIdleEventsFile))
                {
                    return;
                }

                var events = LoadPendingIdleEvents();
                int removed = events.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    return;
                }

                if (events.Count == 0)
                {
                    File.Delete(pendingIdleEventsFile);
                }
                else
                {
                    var json = JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(pendingIdleEventsFile, json, Encoding.UTF8);
                }
            }
            catch
            {
                // 삭제 실패는 무시
            }
        }

        private List<IdleEventData> LoadPendingIdleEvents()
        {
            try
            {
                if (!File.Exists(pendingIdleEventsFile))
                {
                    return new List<IdleEventData>();
                }

                var json = File.ReadAllText(pendingIdleEventsFile, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<IdleEventData>>(json, JsonOptions) ?? new List<IdleEventData>();
            }
            catch
            {
                return new List<IdleEventData>();
            }
        }
    }

    public class WorkTimeInfo
    {
        public string WorkStart { get; set; } = "09:30";
        public string WorkEnd { get; set; } = "17:30";
        public string PcShutdownTime { get; set; } = "17:30";
        public string LunchStart { get; set; } = "12:30";
        public string LunchEnd { get; set; } = "13:30";
        public int IdleThresholdMinutes { get; set; } = 10;
        public List<string> ProhibitedUrls { get; set; } = new List<string>();
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

    // 영업금지 URL API 응답
    public class BanUrlsResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("server_time")]
        public string ServerTime { get; set; } = "";

        [JsonPropertyName("next_since")]
        public string? NextSince { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("rows")]
        public List<BanUrlRow> Rows { get; set; } = new List<BanUrlRow>();

        [JsonPropertyName("reset_at")]
        public string? ResetAt { get; set; }

        [JsonPropertyName("reset_required")]
        public bool ResetRequired { get; set; } = false;
    }

    public class BanUrlRow
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("company_name")]
        public string CompanyName { get; set; } = "";

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = "";

        [JsonPropertyName("deleted")]
        public bool Deleted { get; set; } = false;
    }

    // 로컬 캐시 구조
    public class ProhibitedUrlsCache
    {
        public string? LastSyncTime { get; set; }
        public List<BanUrlRow> Urls { get; set; } = new List<BanUrlRow>();
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
        [JsonPropertyName("priority")] public string Priority { get; set; } = "normal"; // "urgent", "high", "normal" (default)
    }

    public class ClientReleaseCheckResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; } = false;
        [JsonPropertyName("needUpdate")] public bool NeedUpdate { get; set; } = false;
        [JsonPropertyName("latest")] public ClientReleaseInfo? Latest { get; set; } = null;
        [JsonPropertyName("message")] public string? Message { get; set; } = string.Empty;
    }

    public class ManagerPermission
    {
        public string Catcode { get; set; } = string.Empty;
        public string Catcode2 { get; set; } = string.Empty;
        public string Catcode3 { get; set; } = string.Empty;
    }

    public class ManagerInfoDto
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class ManagerInfoResponse
    {
        public bool Success { get; set; }
        public ManagerInfoDto? Manager { get; set; }
        public List<ManagerPermission>? Permissions { get; set; }
    }

    public class ManagerNotificationItem
    {
        public string Id { get; set; } = string.Empty;
        public string NotificationStatus { get; set; } = string.Empty;
        public OvertimeRequestSummary OvertimeRequest { get; set; } = new();
    }

    public class OvertimeRequestSummary
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

    public class EmployeeOvertimeRequest
    {
        public string Id { get; set; } = string.Empty;
        public string WorkDate { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Approver { get; set; } = string.Empty;
    }
}
