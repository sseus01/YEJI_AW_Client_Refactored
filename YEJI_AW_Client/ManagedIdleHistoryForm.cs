using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public class ManagedIdleHistoryForm : Form
    {
        private readonly string serverBaseUrl;
        private readonly HttpClient httpClient;
        private readonly string managerEmpId;

        private ComboBox orgCombo;
        private ComboBox userCombo;
        private DateTimePicker startPicker;
        private DateTimePicker endPicker;
        private Button searchButton;
        private ListView listView;
        private Label emptyLabel;

        private static readonly TimeZoneInfo KoreaTz = SafeGetKoreaTz();
        private string? managerDisplayName;

        public ManagedIdleHistoryForm(string serverBaseUrl, HttpClient httpClient, string managerEmpId)
        {
            this.serverBaseUrl = serverBaseUrl.TrimEnd('/');
            this.httpClient = httpClient;
            this.managerEmpId = managerEmpId;
            InitializeUi();
            Load += async (s, e) =>
            {
                await LoadManagerDisplayNameAsync();
                await LoadOrganizationsAsync();
                EnsurePersonalDefaultShown();
                await LoadIdleEventsAsync();
            };
        }

        private void InitializeUi()
        {
            Text = "관리: 조직 자리비움 이력";
            ClientSize = new Size(900, 540);
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            orgCombo = new ComboBox { Left = 12, Top = 12, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            userCombo = new ComboBox { Left = 280, Top = 12, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };

            startPicker = new DateTimePicker { Left = 530, Top = 12, Width = 160, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
            endPicker = new DateTimePicker { Left = 700, Top = 12, Width = 160, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
            startPicker.Value = DateTime.Today;
            endPicker.Value = DateTime.Today;

            searchButton = new Button { Left = 12, Top = 44, Width = 120, Height = 26, Text = "조회" };
            searchButton.Click += async (s, e) => await LoadIdleEventsAsync();

            listView = new ListView { Left = 12, Top = 76, Width = 848, Height = 452, View = View.Details, FullRowSelect = true, GridLines = true };
            listView.Columns.Add("사번", 100);
            listView.Columns.Add("이름", 140);
            // PC 컬럼 제거 요청 반영
            listView.Columns.Add("시작시간", 170);
            listView.Columns.Add("종료시간", 170);
            listView.Columns.Add("자리비움시간", 120);
            listView.Columns.Add("상세사유", 220);

            emptyLabel = new Label
            {
                Text = "자리비움 이력이 없습니다.",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.None,
                Left = listView.Left,
                Top = listView.Top,
                Width = listView.Width,
                Height = listView.Height,
                Visible = false,
                ForeColor = Color.Gray,
                BackColor = Color.White,
                Font = new Font(Font, FontStyle.Regular)
            };

            Controls.Add(orgCombo);
            Controls.Add(userCombo);
            Controls.Add(startPicker);
            Controls.Add(endPicker);
            Controls.Add(searchButton);
            Controls.Add(listView);
            Controls.Add(emptyLabel);
            emptyLabel.BringToFront();

            orgCombo.SelectedIndexChanged += async (s, e) => await LoadUsersForSelectedOrgAsync();
            // 자동 조회 제거: 조회 버튼으로만 이력 로딩
            // userCombo.SelectedIndexChanged += async (s, e) => await LoadIdleEventsAsync();

            PositionNearTray();
        }

        private async Task LoadManagerDisplayNameAsync()
        {
            try
            {
                string url = $"{serverBaseUrl}/api/client/manager-info?employeeId={Uri.EscapeDataString(managerEmpId)}";
                using var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
#if DEBUG
                    DebugLog("manager-info 응답", json);
#endif
                    var mgr = JsonSerializer.Deserialize<ManagerInfoResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    managerDisplayName = mgr?.Manager?.DisplayName;
                    if (string.IsNullOrWhiteSpace(managerDisplayName))
                    {
                        managerDisplayName = mgr?.Manager?.Username;
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugLog("manager-info 예외", ex.ToString());
#endif
            }
        }

        private async Task LoadOrganizationsAsync()
        {
            try
            {
                string url = $"{serverBaseUrl}/api/client/manager-orgs?employeeId={Uri.EscapeDataString(managerEmpId)}";
                using var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var orgs = JsonSerializer.Deserialize<List<OrgDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                    if (orgs.Count > 0)
                    {
                        PopulateOrgCombo(orgs);
                        return;
                    }
                }
            }
            catch { }

            try
            {
                string url = $"{serverBaseUrl}/api/client/manager-info?employeeId={Uri.EscapeDataString(managerEmpId)}";
                using var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                var mgr = JsonSerializer.Deserialize<ManagerInfoResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var list = new List<OrgDto>();
                if (mgr?.Permissions != null)
                {
                    foreach (var p in mgr.Permissions)
                    {
                        var parts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(p.Catcode)) parts.Add(p.Catcode);
                        if (!string.IsNullOrWhiteSpace(p.Catcode2)) parts.Add(p.Catcode2);
                        if (!string.IsNullOrWhiteSpace(p.Catcode3)) parts.Add(p.Catcode3);
                        var code = string.Join("/", parts);
                        if (string.IsNullOrWhiteSpace(code)) continue;
                        list.Add(new OrgDto { Code = code, Name = code });
                    }
                }

                if (list.Count == 0)
                {
                    list.Add(new OrgDto { Code = "ALL", Name = "전체" });
                }

                PopulateOrgCombo(list);
            }
            catch
            {
                orgCombo.Items.Clear();
                orgCombo.Items.Add(new ComboItem("전체", "ALL"));
                orgCombo.SelectedIndex = 0;
            }
        }

        private void PopulateOrgCombo(List<OrgDto> orgs)
        {
            orgCombo.Items.Clear();
            foreach (var o in orgs)
            {
                orgCombo.Items.Add(new ComboItem(o.Name, string.IsNullOrWhiteSpace(o.Code) ? o.Name : o.Code));
            }
            if (orgCombo.Items.Count == 0)
            {
                orgCombo.Items.Add(new ComboItem("전체", "ALL"));
            }
            orgCombo.SelectedIndex = 0;
        }

        private void EnsurePersonalDefaultShown()
        {
            // 사용자 목록이 비어 있을 때만 개인 기본 항목 추가
            if (userCombo.Items.Count == 0)
            {
                var name = string.IsNullOrWhiteSpace(managerDisplayName) ? "개인" : managerDisplayName.Trim();
                userCombo.Items.Add(new ComboItem($"{name} ({managerEmpId})", managerEmpId));
                userCombo.SelectedIndex = 0;
            }
        }

        private async Task LoadUsersForSelectedOrgAsync()
        {
            var selected = orgCombo.SelectedItem as ComboItem;
            string orgValue = selected?.Value ?? "ALL"; // 코드 또는 경로
            string orgText = selected?.Text ?? orgValue; // 표시명

            var url = new StringBuilder();
            url.Append($"{serverBaseUrl}/api/client/manager-users?");
            url.Append($"orgCode={Uri.EscapeDataString(orgValue)}");
            url.Append($"&orgName={Uri.EscapeDataString(orgText)}");
            url.Append($"&orgPath={Uri.EscapeDataString(orgValue)}");
            url.Append($"&managerId={Uri.EscapeDataString(managerEmpId)}");
            url.Append($"&employeeId={Uri.EscapeDataString(managerEmpId)}");
            url.Append($"&empNo={Uri.EscapeDataString(managerEmpId)}");

#if DEBUG
            DebugLog("manager-users 요청", url.ToString());
#endif

            Dictionary<string, string> users = new();
            try
            {
                using var response = await httpClient.GetAsync(url.ToString());
#if DEBUG
                DebugLog("manager-users 상태", $"Status: {(int)response.StatusCode} {response.ReasonPhrase}");
#endif
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
#if DEBUG
                    DebugLog("manager-users 응답", json.Length > 4000 ? json.Substring(0, 4000) : json);
#endif
                    users = ParseUsers(json, orgValue);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugLog("manager-users 예외", ex.ToString());
#endif
            }

            if (users.Count == 0)
            {
                try
                {
                    var end = DateTime.Today;
                    var start = end.AddDays(-30);
                    string murl = $"{serverBaseUrl}/api/client/manager-logs?employeeId={Uri.EscapeDataString(managerEmpId)}&startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}";
#if DEBUG
                    DebugLog("manager-logs 요청", murl);
#endif
                    string json = await httpClient.GetStringAsync(murl);
#if DEBUG
                    DebugLog("manager-logs 응답", json.Length > 4000 ? json.Substring(0, 4000) : json);
#endif
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    static IEnumerable<JsonElement> EnumerateItems(JsonElement element)
                    {
                        if (element.ValueKind == JsonValueKind.Array)
                        {
                            return element.EnumerateArray();
                        }

                        if (element.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                        {
                            return dataArr.EnumerateArray();
                        }

                        return Enumerable.Empty<JsonElement>();
                    }

                    foreach (var item in EnumerateItems(root))
                    {
                        if (!MatchesOrganization(item, orgValue))
                            continue;

                        string id = GetProp(item, "employeeId", "empNo", "emp_no", "id", "employee_id");
                        string name = GetProp(item, "employeeName", "empName", "emp_name", "name", "displayName");
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            users[id] = string.IsNullOrWhiteSpace(name) ? id : name;
                        }
                    }
                }
                catch (Exception ex)
                {
#if DEBUG
                    DebugLog("manager-logs 예외", ex.ToString());
#endif
                }
            }

            PopulateUserComboFromDict(users);

            // 본인 사번이 목록에 있으면 기본 선택
            if (userCombo.Items.Cast<object>().FirstOrDefault(it => it is ComboItem ci && ci.Value == managerEmpId) is ComboItem myItem)
            {
                userCombo.SelectedItem = myItem;
            }
            else if (userCombo.Items.Count == 0)
            {
                var name = string.IsNullOrWhiteSpace(managerDisplayName) ? "개인" : managerDisplayName.Trim();
                userCombo.Items.Add(new ComboItem($"{name} ({managerEmpId})", managerEmpId));
                userCombo.SelectedIndex = 0;
            }
            else
            {
                userCombo.SelectedIndex = 0;
            }
        }

        private static string GetProp(JsonElement element, params string[] names)
        {
            foreach (var n in names)
            {
                if (element.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null)
                {
                    return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : v.ToString();
                }
            }
            return string.Empty;
        }

        private static bool MatchesOrganization(JsonElement item, string orgValue)
        {
            if (string.IsNullOrWhiteSpace(orgValue) || string.Equals(orgValue, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool ContainsOrg(string? target)
            {
                if (string.IsNullOrWhiteSpace(target)) return false;
                return target.IndexOf(orgValue, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (ContainsOrg(GetProp(item, "orgCode", "orgPath", "org", "orgName", "organization", "department", "departmentPath", "dept", "deptPath")))
            {
                return true;
            }

            // catcode 조합으로 표현된 조직 경로도 함께 비교
            var catcodeParts = new[] { GetProp(item, "catcode"), GetProp(item, "catcode2"), GetProp(item, "catcode3") }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (catcodeParts.Count > 0)
            {
                var combined = string.Join("/", catcodeParts);
                if (ContainsOrg(combined))
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<string, string> ParseUsers(string json, string orgValue)
        {
            var users = new Dictionary<string, string>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                void AddIfValid(JsonElement el)
                {
                    // 다양한 필드명 지원
                    string id = GetProp(el, "employeeId", "empNo", "emp_no", "id", "userId", "empId");
                    string name = GetProp(el, "displayName", "name", "employeeName", "empName", "emp_name", "userName");
                    if (!string.IsNullOrWhiteSpace(id) && MatchesOrganization(el, orgValue))
                    {
                        users[id] = string.IsNullOrWhiteSpace(name) ? id : name;
                    }
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in root.EnumerateArray()) AddIfValid(it);
                    return users;
                }

                // 배열 필드 찾기
                string[] arrayNames = new[] { "users", "data", "items", "content", "employees", "members" };
                foreach (var n in arrayNames)
                {
                    if (root.TryGetProperty(n, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray()) AddIfValid(it);
                        return users;
                    }
                }

                // 단일/중첩 객체 처리
                foreach (var prop in root.EnumerateObject())
                {
                    var v = prop.Value;
                    if (v.ValueKind == JsonValueKind.Object) AddIfValid(v);
                    else if (v.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in v.EnumerateArray()) AddIfValid(it);
                    }
                }

                AddIfValid(root);
            }
            catch { }

            return users;
        }

        private void PopulateUserComboFromDict(Dictionary<string, string> users)
        {
            userCombo.Items.Clear();
            foreach (var kv in users)
            {
                var name = string.IsNullOrWhiteSpace(kv.Value) ? kv.Key : kv.Value.Trim();
                userCombo.Items.Add(new ComboItem($"{name} ({kv.Key})", kv.Key));
            }
            EnsureManagerSelection();
        }

        private void EnsureManagerSelection()
        {
            ComboItem? managerItem = null;
            foreach (var item in userCombo.Items)
            {
                if (item is ComboItem ci && ci.Value == managerEmpId)
                {
                    managerItem = ci;
                    break;
                }
            }

            if (managerItem != null)
            {
                userCombo.SelectedItem = managerItem;
            }

            else if (userCombo.Items.Count > 0)
            {
                userCombo.SelectedIndex = 0;
            }
        }

        private async Task LoadIdleEventsAsync()
        {
            try
            {
                var userItem = userCombo.SelectedItem as ComboItem;
                var empId = userItem?.Value ?? managerEmpId; // 디폴트: 개인

                var startDateKst = startPicker.Value.Date;
                var endDateKst = endPicker.Value.Date;

                var startUtc = ToUtcFromKst(startDateKst);
                var endUtc = ToUtcFromKst(endDateKst.AddDays(1).AddTicks(-1));

                var url = new StringBuilder();
                url.Append($"{serverBaseUrl}/api/idle-events?employeeId={Uri.EscapeDataString(empId)}");
                url.Append($"&startDate={startDateKst:yyyy-MM-dd}&endDate={endDateKst:yyyy-MM-dd}");
                url.Append($"&start={Uri.EscapeDataString(startUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}");
                url.Append($"&end={Uri.EscapeDataString(endUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}");

                string json = await httpClient.GetStringAsync(url.ToString());
                var events = JsonSerializer.Deserialize<List<IdleEvt>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                // 클라이언트 측 필터: 서버가 범위를 적용하지 않는 경우 대비
                var filtered = events.Where(e => InRangeKst(e.IdleStartTime, e.IdleEndTime, startDateKst, endDateKst)).ToList();

                listView.Items.Clear();
                foreach (var e in filtered)
                {
                    string startText = FormatKst(e.IdleStartTime);
                    string endText = FormatKst(e.IdleEndTime);
                    string duration = CalcDurationKst(e.IdleStartTime, e.IdleEndTime);

                    var item = new ListViewItem(new[]
                    {
                        e.EmployeeId ?? string.Empty,
                        e.EmployeeName ?? string.Empty,
                        startText,
                        endText,
                        duration,
                        e.ReasonDetail ?? string.Empty
                    });
                    listView.Items.Add(item);
                }

                ShowEmptyState(filtered.Count == 0, "자리비움 이력이 없습니다.");
            }
            catch
            {
                listView.Items.Clear();
                ShowEmptyState(true, "자리비움 이력을 불러오지 못했습니다.");
            }
        }

        private void ShowEmptyState(bool show, string message)
        {
            emptyLabel.Text = message;
            emptyLabel.Visible = show;
        }

        private void PositionNearTray()
        {
            const int margin = 10;
            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
            Location = new Point(
                Math.Max(margin, workingArea.Right - Width - margin),
                Math.Max(margin, workingArea.Bottom - Height - margin));
        }

        private static DateTime ToUtcFromKst(DateTime kstLocalDateTime)
        {
            var unspecified = DateTime.SpecifyKind(kstLocalDateTime, DateTimeKind.Unspecified);
            var kst = TimeZoneInfo.ConvertTimeToUtc(unspecified, KoreaTz);
            return kst;
        }

        private static bool InRangeKst(string? startIso, string? endIso, DateTime startDateKst, DateTime endDateKst)
        {
            try
            {
                var start = ParseToKst(startIso);
                var end = ParseToKst(endIso);
                var from = startDateKst.Date;
                var to = endDateKst.Date.AddDays(1).AddTicks(-1);
                var s = start ?? end ?? from;
                var e = end ?? start ?? to;
                return s >= from && e <= to;
            }
            catch
            {
                return true; // 파싱 실패 시 표시
            }
        }

        private static DateTime? ParseToKst(string? iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return null;
            if (DateTimeOffset.TryParse(iso, out var dto))
            {
                return TimeZoneInfo.ConvertTime(dto, KoreaTz).DateTime;
            }
            if (DateTime.TryParse(iso, out var dt))
            {
                var dto2 = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
                return TimeZoneInfo.ConvertTime(dto2, KoreaTz).DateTime;
            }
            return null;
        }

        private static string FormatKst(string? iso)
        {
            var kst = ParseToKst(iso);
            return kst.HasValue ? kst.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty;
        }

        private static string CalcDurationKst(string? startIso, string? endIso)
        {
            var s = ParseToKst(startIso);
            var e = ParseToKst(endIso);
            if (s.HasValue && e.HasValue && e.Value > s.Value)
            {
                var span = e.Value - s.Value;
                return span.ToString("hh\\:mm\\:ss");
            }
            return string.Empty;
        }

        private static TimeZoneInfo SafeGetKoreaTz()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "Korea Standard Time", "Korea Standard Time");
            }
        }

        private class ComboItem
        {
            public string Text { get; }
            public string Value { get; }
            public ComboItem(string text, string value) { Text = text; Value = value; }
            public override string ToString() => Text;
        }

        private class OrgDto
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        private class ManagerInfoResponse
        {
            public bool Success { get; set; }
            public System.Collections.Generic.List<ManagerPermission>? Permissions { get; set; }
            public Manager? Manager { get; set; }
        }

        private class ManagerPermission
        {
            public string Catcode { get; set; } = string.Empty;
            public string Catcode2 { get; set; } = string.Empty;
            public string Catcode3 { get; set; } = string.Empty;
        }

        private class Manager
        {
            public string? DisplayName { get; set; }
            public string? Username { get; set; }
        }

        private class IdleEvt
        {
            public string? EmployeeId { get; set; }
            public string? EmployeeName { get; set; }
            public string? ComputerName { get; set; }
            public string? IdleStartTime { get; set; }
            public string? IdleEndTime { get; set; }
            public string? ReasonDetail { get; set; }
        }

#if DEBUG
        private void DebugLog(string title, string content)
        {
            Debug.WriteLine($"[ManagedIdleHistory] {title}: {content}");
        }
#endif
    }
}
