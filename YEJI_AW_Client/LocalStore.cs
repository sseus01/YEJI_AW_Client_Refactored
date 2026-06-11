using System.Text;
using System.Text.Json;

namespace YEJI_AW_Client;

/// <summary>
/// C:\ProgramData\YEJI_AW\ 아래 모든 파일 경로와 직렬화 로직을 한 곳에 모아둡니다.
/// Form1은 이 클래스의 정적 메서드만 호출하고 경로 상수는 직접 참조하지 않습니다.
/// </summary>
internal static class LocalStore
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions IndentedOpts = new() { WriteIndented = true };
    private static readonly string Root = @"C:\ProgramData\YEJI_AW";

    // 파일 경로 (ApiClient 및 Form1의 Update 코드에서도 참조)
    internal static readonly string ClientConfigFile        = Path.Combine(Root, "client_config.json");
    internal static readonly string BanUrlsCacheFile        = Path.Combine(Root, "prohibited_urls_cache.json");
    internal static readonly string BanEmailsCacheFile      = Path.Combine(Root, "prohibited_emails_cache.json");
    internal static readonly string PendingIdleEventsFile   = Path.Combine(Root, "pending_idle_events.json");
    internal static readonly string TempDisableStateFile    = Path.Combine(Root, "temp_disable_state.json");
    internal static readonly string ClientVersionFile       = Path.Combine(Root, "client_version.txt");
    internal static readonly string PendingUpdateMarkerFile = Path.Combine(Root, "pending_update.txt");
    internal static readonly string LastUpdateAttemptFile   = Path.Combine(Root, "last_update_attempt.txt");
    internal static readonly string ClientStatusLogFile     = Path.Combine(Root, "client_status.log");

    // ── client config ─────────────────────────────────────────────────────────

    public static WorkTimeInfo? LoadClientConfig()
    {
        try
        {
            if (!File.Exists(ClientConfigFile)) return null;
            return JsonSerializer.Deserialize<WorkTimeInfo>(
                File.ReadAllText(ClientConfigFile, Encoding.UTF8), Opts);
        }
        catch { return null; }
    }

    public static void SaveClientConfig(WorkTimeInfo info)
    {
        try
        {
            EnsureDir(ClientConfigFile);
            File.WriteAllText(ClientConfigFile,
                JsonSerializer.Serialize(info, IndentedOpts), Encoding.UTF8);
        }
        catch { }
    }

    // ── prohibited URLs cache ─────────────────────────────────────────────────

    public static ProhibitedUrlsCache? LoadBanUrlsCache()
    {
        try
        {
            if (!File.Exists(BanUrlsCacheFile)) return null;
            return JsonSerializer.Deserialize<ProhibitedUrlsCache>(
                File.ReadAllText(BanUrlsCacheFile, Encoding.UTF8), Opts);
        }
        catch { return null; }
    }

    public static void SaveBanUrlsCache(ProhibitedUrlsCache cache)
    {
        try
        {
            EnsureDir(BanUrlsCacheFile);
            File.WriteAllText(BanUrlsCacheFile, JsonSerializer.Serialize(cache, Opts), Encoding.UTF8);
        }
        catch { }
    }

    // ── prohibited emails cache ───────────────────────────────────────────────

    public static ProhibitedEmailsCache? LoadBanEmailsCache()
    {
        try
        {
            if (!File.Exists(BanEmailsCacheFile)) return null;
            return JsonSerializer.Deserialize<ProhibitedEmailsCache>(
                File.ReadAllText(BanEmailsCacheFile, Encoding.UTF8), Opts);
        }
        catch { return null; }
    }

    public static void SaveBanEmailsCache(ProhibitedEmailsCache cache)
    {
        try
        {
            EnsureDir(BanEmailsCacheFile);
            File.WriteAllText(BanEmailsCacheFile, JsonSerializer.Serialize(cache, Opts), Encoding.UTF8);
        }
        catch { }
    }

    // ── pending idle events ───────────────────────────────────────────────────

    public static List<IdleEventData> LoadPendingIdleEvents()
    {
        try
        {
            if (!File.Exists(PendingIdleEventsFile)) return new();
            return JsonSerializer.Deserialize<List<IdleEventData>>(
                File.ReadAllText(PendingIdleEventsFile, Encoding.UTF8), Opts) ?? new();
        }
        catch { return new(); }
    }

    public static void SavePendingIdleEvent(IdleEventData data)
    {
        try
        {
            EnsureDir(PendingIdleEventsFile);
            var events = LoadPendingIdleEvents();
            int idx = events.FindIndex(e =>
                string.Equals(e.Id, data.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) events[idx] = data;
            else events.Add(data);
            File.WriteAllText(PendingIdleEventsFile,
                JsonSerializer.Serialize(events, IndentedOpts), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ClientLogger.LogAgent($"Failed to save pending idle event: {ex.Message}", "Err", ex);
        }
    }

    public static void RemovePendingIdleEvent(string id)
    {
        try
        {
            if (!File.Exists(PendingIdleEventsFile)) return;
            var events = LoadPendingIdleEvents();
            if (events.RemoveAll(e =>
                string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) == 0) return;
            if (events.Count == 0)
                File.Delete(PendingIdleEventsFile);
            else
                File.WriteAllText(PendingIdleEventsFile,
                    JsonSerializer.Serialize(events, IndentedOpts), Encoding.UTF8);
        }
        catch { }
    }

    // ── temp disable state ────────────────────────────────────────────────────

    public static int LoadUsedTempDisableCountForToday(string today)
    {
        try
        {
            if (!File.Exists(TempDisableStateFile)) return 0;
            var state = JsonSerializer.Deserialize<TempDisableUsageState>(
                File.ReadAllText(TempDisableStateFile, Encoding.UTF8), Opts);
            if (state == null) return 0;
            return string.Equals(state.Date, today, StringComparison.Ordinal)
                ? Math.Max(0, state.UsedCount)
                : 0;
        }
        catch { return 0; }
    }

    public static void SaveUsedTempDisableCountForToday(string today, int usedCount)
    {
        try
        {
            EnsureDir(TempDisableStateFile);
            var state = new TempDisableUsageState
            {
                Date = today,
                UsedCount = Math.Max(0, usedCount)
            };
            File.WriteAllText(TempDisableStateFile, JsonSerializer.Serialize(state, Opts), Encoding.UTF8);
        }
        catch { }
    }

    // ── client version ────────────────────────────────────────────────────────

    public static string? ReadClientVersion()
    {
        try { return File.Exists(ClientVersionFile) ? File.ReadAllText(ClientVersionFile, Encoding.UTF8).Trim() : null; }
        catch { return null; }
    }

    public static void WriteClientVersion(string version)
    {
        try { EnsureDir(ClientVersionFile); File.WriteAllText(ClientVersionFile, version, Encoding.UTF8); }
        catch { }
    }

    // ── pending update marker ─────────────────────────────────────────────────

    public static string? ReadPendingUpdateVersion()
    {
        try { return File.Exists(PendingUpdateMarkerFile) ? File.ReadAllText(PendingUpdateMarkerFile, Encoding.UTF8).Trim() : null; }
        catch { return null; }
    }

    public static void WritePendingUpdateVersion(string version)
    {
        try { EnsureDir(PendingUpdateMarkerFile); File.WriteAllText(PendingUpdateMarkerFile, version, Encoding.UTF8); }
        catch { }
    }

    public static void DeletePendingUpdateMarker()
    {
        try { if (File.Exists(PendingUpdateMarkerFile)) File.Delete(PendingUpdateMarkerFile); }
        catch { }
    }

    // ── last update attempt ───────────────────────────────────────────────────

    public static string? ReadLastUpdateAttempt()
    {
        try { return File.Exists(LastUpdateAttemptFile) ? File.ReadAllText(LastUpdateAttemptFile, Encoding.UTF8).Trim() : null; }
        catch { return null; }
    }

    public static void WriteLastUpdateAttempt(string version)
    {
        try { EnsureDir(LastUpdateAttemptFile); File.WriteAllText(LastUpdateAttemptFile, version, Encoding.UTF8); }
        catch { }
    }

    // ── client status log ─────────────────────────────────────────────────────

    public static void AppendClientStatusLog(string content)
    {
        try { EnsureDir(ClientStatusLogFile); File.AppendAllText(ClientStatusLogFile, content, Encoding.UTF8); }
        catch { }
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private static void EnsureDir(string filePath)
    {
        string? folder = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);
    }
}
