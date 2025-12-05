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
        private string? managerDefaultOrgCode;

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
                await EnsureManagerOrgCodeAsync();
                await LoadOrganizationsAsync();
                await LoadUsersForSelectedOrgAsync();
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

                    // 서버에서 제공하는 기본 조직 코드를 우선 사용
                    if (!string.IsNullOrWhiteSpace(mgr?.Manager?.DefaultOrgCode))
                    {
                        managerDefaultOrgCode = mgr.Manager.DefaultOrgCode;
                    }

                    // 기존 로직: 권한 목록에서 기본 조직 추출
                    SetManagerDefaultOrgCodeIfEmpty(mgr);

                    // manager 객체에 조직 경로가 있으면 이를 우선 사용
                    if (string.IsNullOrWhiteSpace(managerDefaultOrgCode))
                    {
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("manager", out var managerEl))
                        {
                            var candidate = managerEl.EnumerateObject()
                                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                                .Select(p => p.Value.GetString())
                                .Where(v => !string.IsNullOrWhiteSpace(v) && v.Contains("/"))
                                .OrderByDescending(v => v.Count(c => c == '/'))   // "/" 개수가 많은 경로 우선
                                .ThenByDescending(v => v.Length)                 // 길이가 긴 경로 우선
                                .FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(candidate))
                            {
                                managerDefaultOrgCode = candidate;
                            }
                        }
                    }

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
                SetManagerDefaultOrgCodeIfEmpty(mgr);
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
                SetDefaultOrgSelection();
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
            SetDefaultOrgSelection();
        }

        private void SetManagerDefaultOrgCodeIfEmpty(ManagerInfoResponse? mgr)
        {
            if (mgr?.Permissions == null || mgr.Permissions.Count == 0 || !string.IsNullOrWhiteSpace(managerDefaultOrgCode))
            {
                return;
            }

            // catcode3 → catcode2 → catcode 순으로 가장 깊은 조직을 찾음
            ManagerPermission? selected = mgr.Permissions
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Catcode3))
                ?? mgr.Permissions.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Catcode2))
                ?? mgr.Permissions.FirstOrDefault();

            if (selected == null) return;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(selected.Catcode)) parts.Add(selected.Catcode);
            if (!string.IsNullOrWhiteSpace(selected.Catcode2)) parts.Add(selected.Catcode2);
            if (!string.IsNullOrWhiteSpace(selected.Catcode3)) parts.Add(selected.Catcode3);

            if (parts.Count > 0)
            {
                managerDefaultOrgCode = string.Join("/", parts);
            }
        }

        private void SetDefaultOrgSelection()
        {
#if DEBUG
            DebugLog("SetDefaultOrgSelection 시작", $"managerDefaultOrgCode={managerDefaultOrgCode}, 콤보박스 항목 수={orgCombo.Items.Count}");
#endif
            if (!string.IsNullOrWhiteSpace(managerDefaultOrgCode))
            {
                string target = NormalizeOrg(managerDefaultOrgCode);
#if DEBUG
                DebugLog("조직 선택 대상", $"target={target}");
                for (int i = 0; i < orgCombo.Items.Count; i++)
                {
                    if (orgCombo.Items[i] is ComboItem ci)
                    {
                        DebugLog($"콤보박스 항목 [{i}]", $"Text={ci.Text}, Value={ci.Value}");
                    }
                }
#endif

                // 먼저 정확히 일치하는 항목 찾기
                var exactMatch = orgCombo.Items.Cast<object>()
                    .Select((item, idx) => (item, idx))
                    .FirstOrDefault(tuple => tuple.item is ComboItem ci &&
                      (string.Equals(NormalizeOrg(ci.Value), target, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(NormalizeOrg(ci.Text), target, StringComparison.OrdinalIgnoreCase)));

                if (exactMatch.item is ComboItem exactCi)
                {
#if DEBUG
                    DebugLog("정확한 일치 발견", $"idx={exactMatch.idx}, Text={exactCi.Text}, Value={exactCi.Value}");
#endif
                    orgCombo.SelectedIndex = exactMatch.idx;
                    return;
                }

                // 정확히 일치하는 항목이 없으면 부분 일치 시도
                var partialMatch = orgCombo.Items.Cast<object>()
                    .Select((item, idx) => (item, idx))
                    .FirstOrDefault(tuple => tuple.item is ComboItem ci &&
                      (IsOrgMatch(ci.Value, target) || IsOrgMatch(ci.Text, target)));

                if (partialMatch.item is ComboItem partialCi)
                {
#if DEBUG
                    DebugLog("부분 일치 발견", $"idx={partialMatch.idx}, Text={partialCi.Text}, Value={partialCi.Value}");
#endif
                    orgCombo.SelectedIndex = partialMatch.idx;
                    return;
                }
#if DEBUG
                DebugLog("일치하는 조직 없음", "기본값으로 설정");
#endif
            }

            orgCombo.SelectedIndex = orgCombo.Items.Count > 0 ? 0 : -1;
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
            string? managerUsersJson = null;
            try
            {
                using var response = await httpClient.GetAsync(url.ToString());
#if DEBUG
                DebugLog("manager-users 상태", $"Status: {(int)response.StatusCode} {response.ReasonPhrase}");
#endif
                if (response.IsSuccessStatusCode)
                {
                    managerUsersJson = await response.Content.ReadAsStringAsync();
#if DEBUG
                    DebugLog("manager-users 응답", managerUsersJson.Length > 4000 ? managerUsersJson.Substring(0, 4000) : managerUsersJson);
#endif
                    users = ParseUsers(managerUsersJson, orgValue);
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

            EnsureManagerUserExists(users, orgValue);
            await EnsureManagerOrgCodeFromUsersAsync(managerUsersJson, orgValue);
            PopulateUserComboFromDict(users);

            // 본인 사번이 목록에 있으면 기본 선택
            if (userCombo.Items.Cast<object>().FirstOrDefault(it => it is ComboItem ci && ci.Value == managerEmpId) is ComboItem myItem)
            {
                userCombo.SelectedItem = myItem;
            }
            else if (userCombo.Items.Count == 0)
            {
                userCombo.Items.Add(new ComboItem("담당자 없음", string.Empty));
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

        private void EnsureManagerUserExists(Dictionary<string, string> users, string orgValue)
        {
            if (string.IsNullOrWhiteSpace(managerEmpId))
            {
                return;
            }

            // 관리자가 속한 기본 조직이 아닌 경우에는 노출하지 않는다
            if (!string.IsNullOrWhiteSpace(managerDefaultOrgCode) &&
                !string.Equals(orgValue, managerDefaultOrgCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var display = string.IsNullOrWhiteSpace(managerDisplayName) ? managerEmpId : managerDisplayName.Trim();
            if (users.ContainsKey(managerEmpId))
            {
                if (string.IsNullOrWhiteSpace(users[managerEmpId]))
                {
                    users[managerEmpId] = display;
                }
                return;
            }

            users[managerEmpId] = display;
        }

        private async Task EnsureManagerOrgCodeAsync()
        {
            try
            {
                var url = new StringBuilder();
                url.Append($"{serverBaseUrl}/api/client/manager-users?");
                url.Append("orgCode=ALL&orgName=ALL&orgPath=ALL&");
                url.Append($"managerId={Uri.EscapeDataString(managerEmpId)}&");
                url.Append($"employeeId={Uri.EscapeDataString(managerEmpId)}&empNo={Uri.EscapeDataString(managerEmpId)}");

                using var response = await httpClient.GetAsync(url.ToString());
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                bool allowOverride = string.IsNullOrWhiteSpace(managerDefaultOrgCode);
                TrySetManagerOrgFromJson(json, "ALL", allowOverride);
            }
            catch
            {
                // 무시: 기본 조직을 설정하지 못해도 실행은 계속한다
            }
        }

        private async Task EnsureManagerOrgCodeFromUsersAsync(string? json, string orgValue)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            bool allowOverride = string.IsNullOrWhiteSpace(managerDefaultOrgCode);
            await Task.Run(() => TrySetManagerOrgFromJson(json, orgValue, allowOverride));
        }

        private bool TrySetManagerOrgFromJson(string json, string orgValue, bool allowOverride)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool TryHandleElement(JsonElement el)
                {
                    string id = GetProp(el, "employeeId", "empNo", "emp_no", "id", "userId", "empId");
                    if (!string.Equals(id, managerEmpId, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (!allowOverride && !string.IsNullOrWhiteSpace(managerDefaultOrgCode))
                    {
                        return true;
                    }

                    string path = ExtractOrgPath(el);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        return false;
                    }

                    // orgValue가 ALL일 때만 덮어쓰거나, 동일 경로일 때 설정
                    if (string.Equals(orgValue, "ALL", StringComparison.OrdinalIgnoreCase) || MatchesOrganization(el, orgValue))
                    {
                        managerDefaultOrgCode = NormalizeOrg(path);
                        return true;
                    }

                    return false;
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in root.EnumerateArray())
                    {
                        if (TryHandleElement(it)) return true;
                    }
                    return false;
                }

                string[] arrayNames = new[] { "users", "data", "items", "content", "employees", "members" };
                foreach (var n in arrayNames)
                {
                    if (root.TryGetProperty(n, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray())
                        {
                            if (TryHandleElement(it)) return true;
                        }
                    }
                }

                foreach (var prop in root.EnumerateObject())
                {
                    var v = prop.Value;
                    if (v.ValueKind == JsonValueKind.Object && TryHandleElement(v)) return true;
                    if (v.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in v.EnumerateArray())
                        {
                            if (TryHandleElement(it)) return true;
                        }
                    }
                }

                return TryHandleElement(root);
            }
            catch
            {
                // 기본 조직을 찾지 못한 경우 그대로 진행
            }

            return false;
        }

        private static string ExtractOrgPath(JsonElement el)
        {
            string[] fields = new[] { "orgPath", "org", "orgName", "organization", "department", "departmentPath", "dept", "deptPath" };
            foreach (var f in fields)
            {
                var val = GetProp(el, f);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return NormalizeOrg(val);
                }
            }

            var parts = new[] { GetProp(el, "catcode"), GetProp(el, "catcode2"), GetProp(el, "catcode3") }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim());
            var combined = string.Join("/", parts);
            return NormalizeOrg(combined);
        }

        private static string NormalizeOrg(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static bool IsOrgMatch(string? source, string target)
        {
            var normalized = NormalizeOrg(source);
            if (string.Equals(normalized, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalized.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   target.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0;
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
                var empId = userItem?.Value ?? string.Empty;

                if (string.IsNullOrWhiteSpace(empId))
                {
                    listView.Items.Clear();
                    ShowEmptyState(true, "선택된 담당자가 없습니다.");
                    return;
                }

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
            public string? DefaultOrgCode { get; set; }
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