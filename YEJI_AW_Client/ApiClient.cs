using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace YEJI_AW_Client;

/// <summary>
/// 서버 HTTP 호출 전담 클래스.
/// HttpClient·JsonOptions·BaseUrl 등 공용 인프라를 한 곳에서 관리하고,
/// Form1 에서 직접 HttpClient.XxxAsync() 를 호출하는 코드를 제거합니다.
/// </summary>
internal sealed class ApiClient
{
    // ── 공용 인프라 (Form1 도 참조 가능) ────────────────────────────────────
    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    internal static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    internal const string BaseUrl = "http://aw.ye-ji.kr";

    // ── 연결 상태 추적 ────────────────────────────────────────────────────────
    private static int _consecutiveFailures;
    private const int OfflineThreshold = 3;
    internal static bool IsServerReachable { get; private set; } = true;
    internal static event EventHandler<bool>? ServerReachabilityChanged;

    // ── 인스턴스 필드 ─────────────────────────────────────────────────────────
    private readonly string _employeeId;
    private readonly string _employeeName;
    private readonly string _computerName;

    internal record struct ClientStatusResult(bool Success, bool UserNotFound);

    public ApiClient(string employeeId, string employeeName, string computerName)
    {
        _employeeId = employeeId;
        _employeeName = employeeName;
        _computerName = computerName;
    }

    // ── 재시도 / 연결 상태 헬퍼 ──────────────────────────────────────────────

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException ||
        ex is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested;

    private static void OnRequestSuccess()
    {
        System.Threading.Interlocked.Exchange(ref _consecutiveFailures, 0);
        if (IsServerReachable) return;
        IsServerReachable = true;
        ServerReachabilityChanged?.Invoke(null, true);
    }

    private static void OnNetworkFailure()
    {
        int failures = System.Threading.Interlocked.Increment(ref _consecutiveFailures);
        if (failures < OfflineThreshold || !IsServerReachable) return;
        IsServerReachable = false;
        ServerReachabilityChanged?.Invoke(null, false);
    }

    /// <summary>
    /// GET 요청 + JSON 역직렬화. 네트워크 오류 시 지수 백오프로 최대 maxRetries회 재시도합니다.
    /// 서버가 non-2xx를 반환하면 재시도 없이 즉시 null을 반환합니다.
    /// </summary>
    private static async Task<T?> RetryGetJsonAsync<T>(string url, int maxRetries = 2) where T : class
    {
        int delayMs = 1500;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!res.IsSuccessStatusCode) return null;
                await using var stream = await res.Content.ReadAsStreamAsync();
                var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts);
                OnRequestSuccess();
                return result;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxRetries)
            {
                await Task.Delay(delayMs * (1 << attempt));
            }
            catch { break; }
        }
        OnNetworkFailure();
        return null;
    }

    // ── GET: 설정 ─────────────────────────────────────────────────────────────

    public Task<WorkTimeInfo?> GetClientConfigAsync()
        => RetryGetJsonAsync<WorkTimeInfo>(BaseUrl + "/api/client-config");

    public Task<PcOffSettings?> GetPcOffSettingsAsync()
        => RetryGetJsonAsync<PcOffSettings>(BaseUrl + "/api/pc-off-settings");

    // ── GET: 금지 목록 ────────────────────────────────────────────────────────

    public Task<BanUrlsResponse?> GetBanUrlsAsync(string? since)
    {
        string url = BaseUrl + "/api/client/ban-urls";
        if (!string.IsNullOrWhiteSpace(since)) url += $"?since={Uri.EscapeDataString(since)}";
        return RetryGetJsonAsync<BanUrlsResponse>(url);
    }

    public Task<BanEmailsResponse?> GetBanEmailsAsync(string? since)
    {
        string url = BaseUrl + "/api/client/ban-emails";
        if (!string.IsNullOrWhiteSpace(since)) url += $"?since={Uri.EscapeDataString(since)}";
        return RetryGetJsonAsync<BanEmailsResponse>(url);
    }

    /// <summary>
    /// 영업금지 예외 목록을 파싱하여 "type|value" 키 집합으로 반환합니다.
    /// HTTP 실패 시 null 을 반환합니다 (Form1 에서 이전 캐시를 유지).
    /// </summary>
    public async Task<HashSet<string>?> GetBanItemExceptionKeysAsync(string employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string url = $"{BaseUrl}/api/client/ban-item-exceptions?employeeId={Uri.EscapeDataString(employeeId)}";
            using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();

            await using var stream = await res.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            JsonElement rowsElement = doc.RootElement;
            if (rowsElement.ValueKind == JsonValueKind.Object)
            {
                if (rowsElement.TryGetProperty("rows", out var rows))
                    rowsElement = rows;
                else if (rowsElement.TryGetProperty("data", out var data))
                {
                    if (data.ValueKind == JsonValueKind.Array)
                        rowsElement = data;
                    else if (data.ValueKind == JsonValueKind.Object &&
                             data.TryGetProperty("rows", out var dataRows))
                        rowsElement = dataRows;
                }
            }

            var parsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rowsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rowsElement.EnumerateArray())
                {
                    string type  = GetJsonString(row, "type",  "ban_type",  "itemType");
                    string value = GetJsonString(row, "value", "itemValue", "url", "email");
                    if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value))
                        continue;
                    string nt = NormalizeBanItemType(type);
                    string nv = NormalizeBanItemValue(nt, value);
                    if (!string.IsNullOrWhiteSpace(nv))
                        parsed.Add(BuildBanExceptionKey(nt, nv));
                }
            }
            return parsed;
        }
        catch { return null; }
    }

    // ── GET: 팝업 스케줄 ──────────────────────────────────────────────────────

    public Task<List<PopupSchedule>?> GetPopupSchedulesAsync()
        => RetryGetJsonAsync<List<PopupSchedule>>(BaseUrl + "/api/scheduled-popups");

    // ── GET: 이미지 다운로드 & 리사이즈 ──────────────────────────────────────

    public static async Task<Image?> GetScaledImageAsync(string url, int maxWidth, int maxHeight)
    {
        try
        {
            using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            await using var stream = await res.Content.ReadAsStreamAsync();
            using var original = Image.FromStream(stream);

            var targetSize = GetScaledSize(original.Size, maxWidth, maxHeight);
            using var resized = new Bitmap(targetSize.Width, targetSize.Height);
            using (var g = Graphics.FromImage(resized))
            {
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.InterpolationMode  = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode      = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.DrawImage(original, 0, 0, targetSize.Width, targetSize.Height);
            }

            var optimized = new Bitmap(resized.Width, resized.Height, PixelFormat.Format16bppRgb565);
            using (var tg = Graphics.FromImage(optimized))
                tg.DrawImage(resized, 0, 0, resized.Width, resized.Height);
            return optimized;
        }
        catch { return null; }
    }

    private static Size GetScaledSize(Size original, int maxWidth, int maxHeight)
    {
        float ratioW = (float)maxWidth  / original.Width;
        float ratioH = (float)maxHeight / original.Height;
        float ratio  = Math.Min(ratioW, ratioH);
        if (ratio >= 1f) return original;
        return new Size((int)(original.Width * ratio), (int)(original.Height * ratio));
    }

    public static string ResolveImageUrl(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out _)) return imageUrl;
        return $"{BaseUrl.TrimEnd('/')}/{imageUrl.TrimStart('/')}";
    }

    // ── GET: 업데이트 확인 ────────────────────────────────────────────────────

    public Task<ClientReleaseCheckResponse?> GetClientUpdateInfoAsync(string currentVersion)
        => RetryGetJsonAsync<ClientReleaseCheckResponse>(
            $"{BaseUrl}/api/client-releases/check?currentVersion={Uri.EscapeDataString(currentVersion)}");

    // ── GET: 자리비움 이력 ─────────────────────────────────────────────────────

    public async Task<List<IdleEventData>> GetIdleEventsAsync(
        string empId, DateTime fromDate, DateTime toDate)
    {
        try
        {
            string url = $"{BaseUrl}/api/idle-events?employeeId={Uri.EscapeDataString(empId)}" +
                         $"&startDate={fromDate:yyyy-MM-dd}&endDate={toDate:yyyy-MM-dd}";
            string json = await Http.GetStringAsync(url);
            return JsonSerializer.Deserialize<List<IdleEventData>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    // ── GET: 연장근무 ──────────────────────────────────────────────────────────

    public async Task<List<EmployeeOvertimeRequest>> GetOvertimeRequestsAsync(
        string empId, DateTime fromDate, DateTime toDate)
    {
        var list = new List<EmployeeOvertimeRequest>();
        try
        {
            string url = $"{BaseUrl}/api/overtime-requests?employeeId={Uri.EscapeDataString(empId)}" +
                         $"&startDate={fromDate:yyyy-MM-dd}&endDate={toDate:yyyy-MM-dd}";
            using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) return list;

            string json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            foreach (var item in EnumerateArrayLike(doc.RootElement))
            {
                var r = new EmployeeOvertimeRequest
                {
                    Id         = GetElementString(item, "id", "requestId", "request_id"),
                    WorkDate   = GetElementString(item, "workDate", "work_date", "date"),
                    StartTime  = GetElementString(item, "startTime", "start_time", "start"),
                    EndTime    = GetElementString(item, "endTime", "end_time", "end"),
                    Reason     = GetElementString(item, "reason", "description", "comment"),
                    Status     = GetElementString(item, "status", "approvalStatus", "approval_status", "result"),
                    Approver   = GetElementString(item, "approver", "approverName", "approvedBy", "approved_by", "approver_name")
                };
                if (!string.IsNullOrWhiteSpace(r.Id)) list.Add(r);
            }
        }
        catch { }
        return list;
    }

    /// <summary>오늘 승인된 연장근무 중 가장 늦게 끝나는 종료 시각을 반환합니다.</summary>
    public async Task<DateTime?> GetApprovedOvertimeEndTimeAsync(string empId, DateTime now)
    {
        try
        {
            string today = now.ToString("yyyy-MM-dd");
            string url = $"{BaseUrl}/api/overtime-requests?employeeId={Uri.EscapeDataString(empId)}" +
                         $"&startDate={today}&endDate={today}";
            using var res = await Http.GetAsync(url);
            if (!res.IsSuccessStatusCode) return null;

            string json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            DateTime? currentEnd = null;
            DateTime? nextEnd    = null;
            DateTime? nextStart  = null;

            foreach (var entry in EnumerateArrayLike(doc.RootElement))
            {
                string status = GetElementString(entry, "status", "approvalStatus", "approval_status", "result");
                if (!string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase)) continue;

                string workDate = GetElementString(entry, "workDate", "work_date", "date");
                if (!DateTime.TryParse(workDate, out var pd) || pd.Date != now.Date) continue;

                string startStr = GetElementString(entry, "startTime", "start_time", "start");
                string endStr   = GetElementString(entry, "endTime",   "end_time",   "end");
                if (!TimeSpan.TryParse(startStr, out var startTs) ||
                    !TimeSpan.TryParse(endStr, out var endTs)) continue;

                DateTime startDt = now.Date.Add(startTs);
                DateTime endDt   = now.Date.Add(endTs);
                if (endDt <= now) continue;

                if (now >= startDt && now < endDt)
                {
                    if (currentEnd == null || endDt > currentEnd.Value) currentEnd = endDt;
                }
                else if (now < startDt)
                {
                    if (nextStart == null || startDt < nextStart.Value)
                    {
                        nextStart = startDt;
                        nextEnd   = endDt;
                    }
                }
            }
            return currentEnd ?? nextEnd;
        }
        catch { return null; }
    }

    public async Task<bool> GetShutdownExceptionAsync(string empId)
    {
        try
        {
            string url = $"{BaseUrl}/api/shutdown-plan?employeeId={Uri.EscapeDataString(empId)}";
            using var res = await Http.GetAsync(url);
            if (!res.IsSuccessStatusCode) return false;

            string json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool disabled = GetElementBool(root, "shutdownDisabled", "shutdown_disabled");
            string mode   = GetElementString(root, "mode", "planMode", "plan_mode");
            return disabled || string.Equals(mode, "EXCEPTION", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ── GET: 관리자 ───────────────────────────────────────────────────────────

    public Task<ManagerInfoResponse?> GetManagerInfoAsync(string empId)
        => RetryGetJsonAsync<ManagerInfoResponse>(
            $"{BaseUrl}/api/client/manager-info?employeeId={Uri.EscapeDataString(empId)}");

    public async Task<List<ManagerNotificationItem>> GetManagerNotificationsAsync(string empId)
    {
        try
        {
            string url = $"{BaseUrl}/api/client/manager-notifications?employeeId={Uri.EscapeDataString(empId)}";
            using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) return new();
            string json = await res.Content.ReadAsStringAsync();
            return ParseManagerNotifications(json);
        }
        catch { return new(); }
    }

    private static List<ManagerNotificationItem> ParseManagerNotifications(string json)
    {
        var list = new List<ManagerNotificationItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var source = root;
            if (root.TryGetProperty("notifications", out var arr)) source = arr;

            foreach (var item in EnumerateArrayLike(source))
            {
                var overtime = item;
                if (item.TryGetProperty("overtimeRequest", out var o))
                    overtime = o;
                else if (item.TryGetProperty("overtime_request", out var o2))
                    overtime = o2;

                list.Add(new ManagerNotificationItem
                {
                    Id                 = GetElementString(item,    "id", "_id", "notificationId"),
                    NotificationStatus = GetElementString(item,    "notificationStatus", "status"),
                    OvertimeRequest    = new OvertimeRequestSummary
                    {
                        Id           = GetElementString(overtime, "id", "_id", "requestId", "request_id"),
                        EmployeeId   = GetElementString(overtime, "employeeId", "employee_id", "empNo", "emp_no"),
                        EmployeeName = GetElementString(overtime, "employeeName", "employee_name", "empName", "emp_name"),
                        WorkDate     = GetElementString(overtime, "workDate", "work_date", "date"),
                        StartTime    = GetElementString(overtime, "startTime", "start_time", "start"),
                        EndTime      = GetElementString(overtime, "endTime",   "end_time",   "end"),
                        Reason       = GetElementString(overtime, "reason", "description", "comment"),
                        Status       = GetElementString(overtime, "status", "approvalStatus", "approval_status", "result")
                    }
                });
            }
        }
        catch { }
        return list;
    }

    // ── POST: 자리비움 이벤트 ─────────────────────────────────────────────────

    /// <summary>자리비움 이벤트 전송. 성공 시 true, 실패 시 false 를 반환합니다.</summary>
    public async Task<bool> PostIdleEventAsync(IdleEventData data)
    {
        try
        {
            string url  = $"{BaseUrl}/api/idle-events";
            string json = JsonSerializer.Serialize(data);
            ClientLogger.LogWeb(
                $"Sending idle event for {data.EmployeeId} ({data.IdleStartTime} ~ {data.IdleEndTime}, reason: {data.ReasonDetail}).",
                "DBG");
            using var content  = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                ClientLogger.LogWeb($"Idle event sent successfully ({data.EmployeeId}, {data.Id}).", "DBG");
                return true;
            }
            string body = await response.Content.ReadAsStringAsync();
            ClientLogger.LogWeb(
                $"Idle event send failed ({data.EmployeeId}) Status {(int)response.StatusCode}, Body: {body}.",
                "Err");
            return false;
        }
        catch (Exception ex)
        {
            ClientLogger.LogWeb($"Idle event send exception ({data.EmployeeId}, {data.Id}).", "Err", ex);
            return false;
        }
    }

    // ── POST: PC 이벤트 ───────────────────────────────────────────────────────

    public async Task PostPcEventAsync(string eventType, string computerIp, DateTime eventTime)
    {
        try
        {
            var data = BuildPcEventData(eventType, computerIp, eventTime);
            using var content  = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            ClientLogger.LogWeb($"Sending PC event {eventType} for {_employeeId} at {eventTime:HH:mm:ss}.", "DBG");
            using var response = await Http.PostAsync(BaseUrl + "/api/pc-events", content);
            if (response.IsSuccessStatusCode)
                ClientLogger.LogWeb($"PC event {eventType} sent successfully for {_employeeId}.", "DBG");
            else
                ClientLogger.LogWeb($"PC event {eventType} failed with status {(int)response.StatusCode}.", "Err");
        }
        catch (Exception ex)
        {
            ClientLogger.LogWeb($"Failed to send PC event {eventType} for {_employeeId}.", "Err", ex);
        }
    }

    /// <summary>Windows 종료 직전에 호출합니다. 비동기 대기가 불가능한 상황에서 사용하세요.</summary>
    public void PostPcEventSync(string eventType, string computerIp, DateTime eventTime)
    {
        try
        {
            var data = BuildPcEventData(eventType, computerIp, eventTime);
            using var content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            ClientLogger.LogWeb($"Sending PC event {eventType} (sync) for {_employeeId} at {eventTime:HH:mm:ss}.", "DBG");
            using var response = Http.PostAsync(BaseUrl + "/api/pc-events", content)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
                ClientLogger.LogWeb($"PC event {eventType} sent successfully for {_employeeId}.", "DBG");
            else
                ClientLogger.LogWeb($"PC event {eventType} failed with status {(int)response.StatusCode}.", "Err");
        }
        catch (Exception ex)
        {
            ClientLogger.LogWeb($"Failed to send PC event {eventType} for {_employeeId}.", "Err", ex);
        }
    }

    private PcEventData BuildPcEventData(string eventType, string computerIp, DateTime eventTime) =>
        new()
        {
            EmployeeId   = _employeeId,
            EmployeeName = _employeeName,
            ComputerName = _computerName,
            ComputerIP   = computerIp,
            EventType    = eventType,
            EventTime    = eventTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
        };

    // ── POST: 클라이언트 상태 (등록 / 하트비트) ───────────────────────────────

    public async Task<ClientStatusResult> RegisterAsync() =>
        await PostClientStatusInternalAsync($"{BaseUrl}/api/client/register");

    public async Task<ClientStatusResult> HeartbeatAsync() =>
        await PostClientStatusInternalAsync($"{BaseUrl}/api/client/heartbeat");

    private async Task<ClientStatusResult> PostClientStatusInternalAsync(string url)
    {
        var payload = BuildClientStatusPayload();
        string json = JsonSerializer.Serialize(payload);
        using var content  = new StringContent(json, Encoding.UTF8, "application/json");
        string endpoint = TryGetEndpoint(url);

        try
        {
            ClientLogger.LogWeb(
                $"Posting client status to {endpoint} for {payload.EmpNo}/{payload.PcName}.",
                "DBG");
            using var response = await Http.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                WriteClientStatusLog(
                    $"[{payload.EmpNo}/{payload.PcName}] {url} 응답 오류",
                    response: response, body: body);
                // 서버가 응답했으므로 연결은 정상 (4xx는 로직 오류)
                OnRequestSuccess();
                return new ClientStatusResult(false,
                    response.StatusCode == HttpStatusCode.NotFound);
            }
            OnRequestSuccess();
            ClientLogger.LogWeb($"[{payload.EmpNo}] POST {endpoint} success.", "DBG");
            return new ClientStatusResult(true, false);
        }
        catch (Exception ex)
        {
            WriteClientStatusLog(
                $"[{payload.EmpNo}/{payload.PcName}] {url} 요청 예외", ex: ex);
            OnNetworkFailure();
            return new ClientStatusResult(false, false);
        }
    }

    private ClientStatusRequest BuildClientStatusPayload() =>
        new()
        {
            EmpNo         = _employeeId,
            EmpName       = _employeeName,
            PcName        = _computerName,
            ClientVersion = GetCurrentVersion(),
            Ip            = GetLocalIPAddress(),
            Installed     = 1,
            Timestamp     = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

    private static void WriteClientStatusLog(
        string context,
        Exception? ex = null,
        HttpResponseMessage? response = null,
        string? body = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}");
            if (response != null)
                sb.AppendLine($"Status: {(int)response.StatusCode} {response.ReasonPhrase}");
            if (!string.IsNullOrWhiteSpace(body))
                sb.AppendLine($"Body: {body}");
            if (ex != null)
                sb.AppendLine($"Error: {ex}");
            sb.AppendLine();

            LocalStore.AppendClientStatusLog(sb.ToString());

            string logMsg = response != null
                ? $"{context} (Status {(int)response.StatusCode})"
                : context;
            ClientLogger.LogWeb(logMsg, "Err", ex);
        }
        catch { }
    }

    // ── 정적 유틸리티 (Form1 에서도 사용) ───────────────────────────────────

    public static string GetLocalIPAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                        return addr.Address.ToString();
                }
            }
        }
        catch { }

        try
        {
            var ip = Dns.GetHostEntry(Dns.GetHostName())
                        .AddressList
                        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip != null) return ip.ToString();
        }
        catch { }

        return "127.0.0.1";
    }

    public static IEnumerable<JsonElement> EnumerateArrayLike(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray()) yield return item;
            yield break;
        }
        foreach (var name in new[] { "data", "items", "content" })
        {
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray()) yield return item;
                yield break;
            }
        }
    }

    public static string GetElementString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.ValueKind != JsonValueKind.Null)
                return value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : value.ToString();
        }
        return string.Empty;
    }

    public static bool GetElementBool(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (!element.TryGetProperty(name, out var value) ||
                value.ValueKind == JsonValueKind.Null) continue;
            if (value.ValueKind == JsonValueKind.True)  return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var b)) return b;
            if (int.TryParse(value.ToString(), out var i)) return i != 0;
        }
        return false;
    }

    public static string GetJsonString(JsonElement row, params string[] names)
    {
        if (row.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var name in names)
        {
            if (row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    public static string NormalizeBanItemType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return string.Empty;
        string n = type.Trim().ToLowerInvariant();
        if (n is "mail" or "e-mail") return "email";
        if (n is "site" or "domain") return "url";
        return n;
    }

    public static string NormalizeBanItemValue(string type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (string.Equals(type, "email", StringComparison.OrdinalIgnoreCase))
            return NormalizeEmail(value);

        string trimmed = value.Trim().ToLowerInvariant();
        if (!string.Equals(type, "url", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        string? domain = BrowserUrlMonitor.ExtractDomain(trimmed);
        if (!string.IsNullOrWhiteSpace(domain))
        {
            string nd = domain.Trim().ToLowerInvariant();
            if (nd.StartsWith("www.", StringComparison.Ordinal)) nd = nd.Substring(4);
            return nd;
        }
        return trimmed;
    }

    public static string BuildBanExceptionKey(string type, string value) => $"{type}|{value}";

    public static string NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();

    internal static string TryGetEndpoint(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;

    private static string GetCurrentVersion()
    {
        try
        {
            string v = System.Windows.Forms.Application.ProductVersion;
            int plus = v.IndexOf('+');
            return plus > 0 ? v.Substring(0, plus) : v;
        }
        catch { return "0.0.0"; }
    }
}
