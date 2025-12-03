using System;
using System.Collections.Generic;
using System.Drawing;
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

        public ManagedIdleHistoryForm(string serverBaseUrl, HttpClient httpClient, string managerEmpId)
        {
            this.serverBaseUrl = serverBaseUrl.TrimEnd('/');
            this.httpClient = httpClient;
            this.managerEmpId = managerEmpId;
            InitializeUi();
            Load += async (s, e) =>
            {
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

            listView = new ListView { Left = 12, Top = 76, Width = 848, Height = 452, View = View.Details, FullRowSelect = true };
            listView.Columns.Add("사번", 80);
            listView.Columns.Add("이름", 120);
            listView.Columns.Add("PC", 160);
            listView.Columns.Add("시작", 200);
            listView.Columns.Add("종료", 200);
            listView.Columns.Add("사유", 200);

            Controls.Add(orgCombo);
            Controls.Add(userCombo);
            Controls.Add(startPicker);
            Controls.Add(endPicker);
            Controls.Add(searchButton);
            Controls.Add(listView);

            orgCombo.SelectedIndexChanged += async (s, e) => await LoadUsersForSelectedOrgAsync();
            userCombo.SelectedIndexChanged += async (s, e) => await LoadIdleEventsAsync();
        }

        private async Task LoadOrganizationsAsync()
        {
            // 1차: 서버 조직 목록 API 시도
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

            // 2차 폴백: 관리자 정보에서 권한 조직 코드 추출
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
                    // 권한 정보가 없으면 최소 한 항목(전체) 제공
                    list.Add(new OrgDto { Code = "ALL", Name = "전체" });
                }

                PopulateOrgCombo(list);
            }
            catch
            {
                // 최종 폴백: 전체만
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
            // 사용자 콤보에 기본으로 '개인' 추가 및 선택
            bool exists = false;
            foreach (var item in userCombo.Items)
            {
                if (item is ComboItem ci && ci.Value == managerEmpId)
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                userCombo.Items.Insert(0, new ComboItem($"개인 ({managerEmpId})", managerEmpId));
            }
            if (userCombo.Items.Count > 0)
            {
                userCombo.SelectedIndex = 0;
            }
        }

        private async Task LoadUsersForSelectedOrgAsync()
        {
            var selected = orgCombo.SelectedItem as ComboItem;
            string orgCode = selected?.Value ?? "ALL";

            // 1차: 서버 사용자 목록 API 시도 (서버가 관리자 식별 필요할 수 있어 함께 전달)
            try
            {
                var url = new StringBuilder();
                url.Append($"{serverBaseUrl}/api/client/manager-users?orgCode={Uri.EscapeDataString(orgCode)}");
                url.Append($"&managerId={Uri.EscapeDataString(managerEmpId)}");
                url.Append($"&employeeId={Uri.EscapeDataString(managerEmpId)}");
                url.Append($"&empNo={Uri.EscapeDataString(managerEmpId)}");

                using var response = await httpClient.GetAsync(url.ToString());
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var users = ParseUsers(json);
                    PopulateUserComboFromDict(users);
                    EnsurePersonalDefaultShown();
                    return;
                }
            }
            catch { }

            // 2차 폴백: 최근 7일 관리자 로그에서 사용자 목록 추출
            try
            {
                var end = DateTime.Today;
                var start = end.AddDays(-7);
                string url = $"{serverBaseUrl}/api/client/manager-logs?employeeId={Uri.EscapeDataString(managerEmpId)}&startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}";
                string json = await httpClient.GetStringAsync(url);
                var users = new Dictionary<string, string>();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        string id = GetProp(item, "employeeId", "empNo", "emp_no", "id", "employee_id");
                        string name = GetProp(item, "employeeName", "empName", "emp_name", "name", "displayName");
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            if (!users.ContainsKey(id)) users[id] = name;
                        }
                    }
                }
                PopulateUserComboFromDict(users);
            }
            catch
            {
                // 마지막 폴백: 개인만
                userCombo.Items.Clear();
            }

            EnsurePersonalDefaultShown();
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
                    string id = GetProp(el, "employeeId", "empNo", "emp_no", "id", "userId");
                    string name = GetProp(el, "displayName", "name", "employeeName", "empName", "emp_name");
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        users[id] = name;
                    }
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in root.EnumerateArray()) AddIfValid(it);
                    return users;
                }

                string[] arrayNames = new[] { "users", "data", "items", "content" };
                foreach (var n in arrayNames)
                {
                    if (root.TryGetProperty(n, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray()) AddIfValid(it);
                        return users;
                    }
                }

                // 단일 객체일 수도 있음
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
                var name = string.IsNullOrWhiteSpace(kv.Value) ? kv.Key : kv.Value;
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

                var startDate = startPicker.Value.Date;
                var endDate = endPicker.Value.Date;

                // 서버 호환성을 위해 날짜/시간 쿼리 모두 전달
                var startUtc = ToUtcFromKst(startDate);
                var endUtc = ToUtcFromKst(endDate.AddDays(1).AddTicks(-1)); // 당일 23:59:59.9999999 KST

                var url = new StringBuilder();
                url.Append($"{serverBaseUrl}/api/idle-events?employeeId={Uri.EscapeDataString(empId)}");
                url.Append($"&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
                url.Append($"&start={Uri.EscapeDataString(startUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}");
                url.Append($"&end={Uri.EscapeDataString(endUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}");

                string json = await httpClient.GetStringAsync(url.ToString());
                var events = JsonSerializer.Deserialize<List<IdleEvt>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                listView.Items.Clear();
                foreach (var e in events)
                {
                    string startText = FormatKst(e.IdleStartTime);
                    string endText = FormatKst(e.IdleEndTime);

                    var item = new ListViewItem(new[]
                    {
                        e.EmployeeId ?? string.Empty,
                        e.EmployeeName ?? string.Empty,
                        e.ComputerName ?? string.Empty,
                        startText,
                        endText,
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
            // kstLocalDateTime는 현지 시스템 로컬이 아닌 'KST 기준'으로 해석해야 한다.
            var unspecified = DateTime.SpecifyKind(kstLocalDateTime, DateTimeKind.Unspecified);
            var kst = TimeZoneInfo.ConvertTimeToUtc(unspecified, KoreaTz);
            return kst;
        }

        private static string FormatKst(string? iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return string.Empty;
            try
            {
                // 오프셋이 있으면 해당 기준으로, 없으면 UTC로 가정 후 KST로 변환
                if (DateTimeOffset.TryParse(iso, out var dto))
                {
                    var kstTime = TimeZoneInfo.ConvertTime(dto, KoreaTz);
                    return kstTime.ToString("yyyy-MM-dd HH:mm");
                }
                if (DateTime.TryParse(iso, out var dt))
                {
                    var unspecified = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                    var dto2 = new DateTimeOffset(unspecified, TimeSpan.Zero);
                    var kstTime = TimeZoneInfo.ConvertTime(dto2, KoreaTz);
                    return kstTime.ToString("yyyy-MM-dd HH:mm");
                }
            }
            catch { }
            return iso;
        }

        private static TimeZoneInfo SafeGetKoreaTz()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
            }
            catch
            {
                // Fallback: UTC+9
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
        }

        private class ManagerPermission
        {
            public string Catcode { get; set; } = string.Empty;
            public string Catcode2 { get; set; } = string.Empty;
            public string Catcode3 { get; set; } = string.Empty;
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
