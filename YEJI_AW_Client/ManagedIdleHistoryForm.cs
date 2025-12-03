using System;
using System.Collections.Generic;
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
            StartPosition = FormStartPosition.CenterParent;
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
            listView.Columns.Add("시작", 170);
            listView.Columns.Add("종료", 170);
            listView.Columns.Add("자리비움시간", 120);
            listView.Columns.Add("사유", 220);

            Controls.Add(orgCombo);
            Controls.Add(userCombo);
            Controls.Add(startPicker);
            Controls.Add(endPicker);
            Controls.Add(searchButton);
            Controls.Add(listView);

            orgCombo.SelectedIndexChanged += async (s, e) => await LoadUsersForSelectedOrgAsync();
            // 자동 조회 제거: 조회 버튼으로만 이력 로딩
            // userCombo.SelectedIndexChanged += async (s, e) => await LoadIdleEventsAsync();
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
                    var mgr = JsonSerializer.Deserialize<ManagerInfoResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    managerDisplayName = mgr?.Manager?.DisplayName;
                    if (string.IsNullOrWhiteSpace(managerDisplayName))
                    {
                        managerDisplayName = mgr?.Manager?.Username;
                    }
                }
            }
            catch { }
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

            // 1차: 조직별 사용자 목록 API 호출 (가능한 모든 파라미터로 시도)
            var url = new StringBuilder();
            url.Append($"{serverBaseUrl}/api/client/manager-users?");
            url.Append($"orgCode={Uri.EscapeDataString(orgValue)}");
            url.Append($"&orgName={Uri.EscapeDataString(orgText)}");
            url.Append($"&orgPath={Uri.EscapeDataString(orgValue)}");
            url.Append($"&managerId={Uri.EscapeDataString(managerEmpId)}");
            url.Append($"&employeeId={Uri.EscapeDataString(managerEmpId)}");
            url.Append($"&empNo={Uri.EscapeDataString(managerEmpId)}");

            Dictionary<string, string> users = new();
            try
            {
                using var response = await httpClient.GetAsync(url.ToString());
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    users = ParseUsers(json);
                }
            }
            catch { }

            // 2차: 다른 엔드포인트 시도 (서버 구현 차이 대비)
            if (users.Count == 0)
            {
                try
                {
                    string altUrl = $"{serverBaseUrl}/api/client/manager-users-by-org?orgName={Uri.EscapeDataString(orgText)}&managerId={Uri.EscapeDataString(managerEmpId)}";
                    using var response2 = await httpClient.GetAsync(altUrl);
                    if (response2.IsSuccessStatusCode)
                    {
                        string json2 = await response2.Content.ReadAsStringAsync();
                        users = ParseUsers(json2);
                    }
                }
                catch { }
            }

            // 3차 폴백: 응답에 직원 목록이 중첩된 경우 탐색
            if (users.Count == 0)
            {
                try
                {
                    string probeUrl = $"{serverBaseUrl}/api/client/manager-org-users?org={Uri.EscapeDataString(orgValue)}";
                    string json3 = await httpClient.GetStringAsync(probeUrl);
                    users = ParseUsers(json3);
                }
                catch { }
            }

            PopulateUserComboFromDict(users);

            if (userCombo.Items.Count == 0)
            {
                // 사용자 없음: 개인만 표시
                var name = string.IsNullOrWhiteSpace(managerDisplayName) ? "개인" : managerDisplayName.Trim();
                userCombo.Items.Add(new ComboItem($"{name} ({managerEmpId})", managerEmpId));
            }

            if (userCombo.Items.Count > 0)
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

        private Dictionary<string, string> ParseUsers(string json)
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
                    if (!string.IsNullOrWhiteSpace(id))
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
            if (userCombo.Items.Count > 0) userCombo.SelectedIndex = 0;
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
            }
            catch
            {
                MessageBox.Show("자리비움 이력을 가져오지 못했습니다.");
            }
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
    }
}
