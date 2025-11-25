#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Imaging;
using Microsoft.Win32;          // 전원(절전/복귀) 이벤트용

// Timer 혼동 방지를 위해 Windows Forms Timer만 사용
using Timer = System.Windows.Forms.Timer;

namespace YEJI_AW_Client
{
    public partial class Form1 : Form
    {
        private ContextMenuStrip? trayMenu;

        // idleTimer 는 Form1.Designer.cs 에 있다고 가정 (디자이너 타이머)
        // private Timer idleTimer;

        private Timer popupTimer;    // 팝업 시간 체크용
        private Timer configTimer;   // 서버 client_config 5분마다 갱신
        private Timer memoryTrimTimer; // 주기적으로 워킹셋 정리
        private Timer heartbeatTimer; // 주기적 상태 전송

        private TimeSpan workStartTime;
        private TimeSpan workEndTime;
        private TimeSpan lunchStartTime;
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

        private DateTime suspendStartTime;
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
        private DateTime popupKeyDate = DateTime.Today;
        private const int PopupImageMaxWidth = 720;
        private const int PopupImageMaxHeight = 560;

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

        public Form1(string employeeName, string employeeId)
        {
            InitializeComponent();

            this.employeeName = employeeName ?? "";
            this.employeeId = employeeId ?? "";
            this.computerIP = GetLocalIPAddress();

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

            this.Load += async (s, e) =>
            {
                this.Hide();
                this.ShowInTaskbar = false;
                await ResendPendingIdleEventsAsync();
                await CheckClientUpdateAsync();
                await RegisterOrUpdateClientAsync();
                heartbeatTimer.Start();
            };

            InitializeTrayMenu();

            // 시작 시 한 번 서버에서 업무시간/점심시간/자리비움 기준분 가져오기
            _ = FetchWorkTimeFromServerAsync();

            // 종료 시 SHUTDOWN 이벤트 전송
            this.FormClosing += Form1_FormClosing;
            // 프로그램 시작 시 BOOT 이벤트 전송
            _ = SendPcEventAsync("BOOT");

            // 전원(절전/복귀) 이벤트 구독
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        }

        // 폼 완전 종료 시 전원 이벤트 구독 해제
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
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
            workStartTime = TimeSpan.Parse(info.WorkStart);
            workEndTime = TimeSpan.Parse(info.WorkEnd);
            lunchStartTime = TimeSpan.Parse(info.LunchStart);
            lunchEndTime = TimeSpan.Parse(info.LunchEnd);
            idleThreshold = TimeSpan.FromMinutes(info.IdleThresholdMinutes);
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

            var popups = await FetchPopupSchedulesAsync();
            if (popups == null || popups.Count == 0) return;

            DateTime now = DateTime.Now;

            foreach (var popup in popups)
            {
                if (string.IsNullOrWhiteSpace(popup.ScheduledTime))
                    continue;

                if (!TimeSpan.TryParse(popup.ScheduledTime.Trim(), out var scheduledTimeSpan))
                    continue;

                DateTime scheduledDateTime = DateTime.Today.Add(scheduledTimeSpan);

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

        // 팝업 표시 (항상 최상단 유지)
        private async Task ShowPopupAsync(PopupSchedule popup)
        {
            var popupForm = new Form
            {
                StartPosition = FormStartPosition.CenterScreen,
                Width = PopupImageMaxWidth + 80,
                Height = PopupImageMaxHeight + 80,
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
                Cursor = Cursors.Hand
            };

            pictureBox.Image = await LoadScaledImageAsync(ServerBaseUrl + popup.ImageUrl, PopupImageMaxWidth, PopupImageMaxHeight);

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
            if (popupKeyDate != DateTime.Today)
            {
                shownPopupTimes.Clear();
                popupKeyDate = DateTime.Today;
            }
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
                var nowTime = DateTime.Now.TimeOfDay;

                bool isLunchBreak = IsLunchBreak(nowTime);
                if (wasInLunchBreak && !isLunchBreak)
                {
                    lastInputTime = DateTime.Now;
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
                        if (!isIdle && (DateTime.Now - lastInputTime) > idleThreshold)
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
                            await SendAfterWorkIdleAsync(idleStartTime, idleEndTime);
                            }
                            isIdle = false;
                        }
                        lastInputTime = currentInputTime;
                    }
                    else
                    {
                        if (!isIdle && (DateTime.Now - lastInputTime) > idleThreshold)
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
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));

            if (!GetLastInputInfo(ref lii))
                return DateTime.Now;

            uint envTicks = (uint)Environment.TickCount;
            uint lastInputTicks = lii.dwTime;
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
            var todayWorkStart = DateTime.Today.Add(workStartTime);
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
                suspendStartTime = DateTime.Now;
            }
            else if (e.Mode == PowerModes.Resume)
            {
                // 절전에서 다시 깨어날 때
                DateTime resumeTime = DateTime.Now;

                var t = resumeTime.TimeOfDay;
                bool isWorkingTime = IsWorkingTime(t);

                if (isWorkingTime)
                {
                    // 근무시간 안에서 덮개를 닫고 있었다면 전체 구간을 자리비움으로 팝업 처리
                    await HandleIdleIntervalAsync(suspendStartTime, resumeTime);
                }

                lastInputTime = resumeTime;
                isIdle = false;
                hasShownPopup = false;
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
            var segments = SplitIdleInterval(start, end);
            foreach (var segment in segments)
            {
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
                ReasonCategory = "업무종료",
                ReasonDetail = "업무외시간",
                ReasonCode = string.Empty,
                ReasonLevel1 = "업무종료",
                ReasonLevel2 = "업무외시간",
                ReasonLevel3 = string.Empty
            };

            bool success = await SendIdleEventAsync(idleEvent);
            if (!success)
            {
                SavePendingIdleEvent(idleEvent);
            }
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

        private async Task RegisterOrUpdateClientAsync()
        {
            try
            {
                var payload = BuildClientStatusPayload();
                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                await HttpClient.PostAsync($"{ServerBaseUrl}/api/client/register", content);
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
                var payload = BuildClientStatusPayload();
                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                await HttpClient.PostAsync($"{ServerBaseUrl}/api/client/heartbeat", content);
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
                   : DateTime.Now;

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

            notifyIcon.ContextMenuStrip = trayMenu;
            notifyIcon.Visible = true;
        }

        private async void OnViewIdleHistory(object? sender, EventArgs e)
        {
            var history = await FetchIdleHistoryFromServerAsync();
            using IdleHistoryForm form = new IdleHistoryForm(history);
            form.ShowDialog();
        }

        private async Task<List<IdleEventData>> FetchIdleHistoryFromServerAsync()
        {
            try
            {
                var client = HttpClient;
                string url = $"{ServerBaseUrl}/api/idle-events?employeeId={employeeId}";
                string json = await client.GetStringAsync(url);
                var history = JsonSerializer.Deserialize<List<IdleEventData>>(json);
                return history ?? new List<IdleEventData>();
            }
            catch
            {
                MessageBox.Show("자리비움 이력을 가져오는데 실패했습니다.");
                return new List<IdleEventData>();
            }
        }

        private void OnEditUserInfo(object? sender, EventArgs e)
        {
            using UserInfoForm userInfoForm = new UserInfoForm();
            userInfoForm.SetUserInfo(employeeName, employeeId);

            if (userInfoForm.ShowDialog() == DialogResult.OK)
            {
                employeeName = userInfoForm.EmployeeName;
                employeeId = userInfoForm.EmployeeId;

                SaveUserInfo(new UserInfo { Name = employeeName, Id = employeeId });

                MessageBox.Show("사용자 정보가 업데이트 되었습니다.");
            }
        }

        private void SaveUserInfo(UserInfo info)
        {
            try
            {
                string folder = @"C:\ProgramData\YEJI_AW";
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                string filePath = Path.Combine(folder, "user_info.json");
                File.WriteAllText(filePath, JsonSerializer.Serialize(info));
            }
            catch
            {
                MessageBox.Show("사용자 정보 저장 중 오류가 발생했습니다.");
            }
        }

        private string GetLocalIPAddress()
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
            }
            return "IP Not Found";
        }
    }

    // 서버 client_config 구조와 동일하게 맞춘 DTO
    public class WorkTimeInfo
    {
        public string WorkStart { get; set; } = "09:30";   // "HH:mm"
        public string WorkEnd { get; set; } = "17:30";
        public string LunchStart { get; set; } = "12:30";
        public string LunchEnd { get; set; } = "13:30";
        public int IdleThresholdMinutes { get; set; } = 10;
    }

    // PC 이벤트(부팅/종료) 전송용 DTO
    public class PcEventData
    {
        [JsonPropertyName("employeeId")]
        public string EmployeeId { get; set; } = "";

        [JsonPropertyName("employeeName")]
        public string EmployeeName { get; set; } = "";

        [JsonPropertyName("computerName")]
        public string ComputerName { get; set; } = "";

        [JsonPropertyName("computerIp")]
        public string ComputerIP { get; set; } = "";

        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = "";   // "BOOT" 또는 "SHUTDOWN"

        [JsonPropertyName("eventTime")]
        public string EventTime { get; set; } = "";   // ISO 문자열
    }

    public class ClientReleaseInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("originalName")]
        public string OriginalName { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; } = string.Empty;

        [JsonPropertyName("uploadedAt")]
        public string UploadedAt { get; set; } = string.Empty;
    }

    public class ClientReleaseCheckResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
            = false;

        [JsonPropertyName("needUpdate")]
        public bool NeedUpdate { get; set; }
            = false;

        [JsonPropertyName("latest")]
        public ClientReleaseInfo? Latest { get; set; }
            = null;

        [JsonPropertyName("message")]
        public string? Message { get; set; }
            = string.Empty;
    }
}
