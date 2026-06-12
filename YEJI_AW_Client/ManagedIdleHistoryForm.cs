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
        private List<OrgWithUsers> managerOrgs = new();
        private bool preferAllOrgOnFirstLoad = true;

        private ComboBox orgCombo;
        private ComboBox userCombo;
        private DateTimePicker startPicker;
        private DateTimePicker endPicker;
        private RoundButton searchButton;
        private RoundButton orgViewButton;
        private ListView listView;
        private Label emptyLabel;
        private Panel filterPanel;

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
            Text            = "관리: 조직 자리비움 이력";
            ClientSize      = new Size(980, 600);
            StartPosition   = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            BackColor       = UiTheme.Background;

            // ── 필터 바 ─────────────────────────────────────────────
            filterPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 46,
                BackColor = UiTheme.Surface
            };

            orgCombo  = new ComboBox { Left = 12,  Top = 11, Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            userCombo = new ComboBox { Left = 240, Top = 11, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };

            startPicker = new DateTimePicker { Left = 448, Top = 11, Width = 110, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
            endPicker   = new DateTimePicker { Left = 566, Top = 11, Width = 110, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
            startPicker.Value = DateTime.Today;
            endPicker.Value   = DateTime.Today;

            searchButton = new RoundButton { Left = 684, Top = 6, Width = 72, Height = UiTheme.BtnH, Text = "조회" };
            UiTheme.StylePrimary(searchButton);
            searchButton.Click += async (s, e) => await LoadIdleEventsAsync();

            orgViewButton = new RoundButton { Left = 764, Top = 6, Width = 80, Height = UiTheme.BtnH, Text = "전체보기", Visible = false };
            UiTheme.StylePrimary(orgViewButton);
            orgViewButton.Click += async (s, e) => await LoadOrgIdleEventsAsync();

            filterPanel.Controls.AddRange(new Control[] { orgCombo, userCombo, startPicker, endPicker, searchButton, orgViewButton });

            // ── ListView ────────────────────────────────────────────
            listView = new ListView
            {
                Dock        = DockStyle.Fill,
                View        = View.Details,
                FullRowSelect = true,
                GridLines   = true,
                BackColor   = UiTheme.Surface,
                BorderStyle = BorderStyle.Fixed3D,
                Font        = UiTheme.Body,
                OwnerDraw   = true
            };
            listView.Columns.Add("사번",       100);
            listView.Columns.Add("이름",       140);
            listView.Columns.Add("시작시간",   170);
            listView.Columns.Add("종료시간",   170);
            listView.Columns.Add("자리비움시간", 120);
            listView.Columns.Add("상세사유",   220);

            var headerDrawFont = new Font(UiTheme.Body.FontFamily, 9F, FontStyle.Bold);
            listView.DrawColumnHeader += (s, e) =>
            {
                using var bg   = new SolidBrush(UiTheme.Primary);
                using var fg   = new SolidBrush(UiTheme.TextOnPrimary);
                e.Graphics.FillRectangle(bg, e.Bounds);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString(e.Header.Text, headerDrawFont, fg, e.Bounds, sf);
            };
            listView.DrawItem += (s, e) => { e.DrawDefault = true; };
            listView.DrawSubItem += (s, e) =>
            {
                var backColor = (e.ItemState & ListViewItemStates.Selected) != 0
                    ? UiTheme.Selection
                    : e.ItemIndex % 2 == 0 ? UiTheme.Surface : UiTheme.Background;
                using var brush = new SolidBrush(backColor);
                e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, e.Bounds, UiTheme.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };

            emptyLabel = new Label
            {
                Text      = "자리비움 이력이 없습니다.",
                AutoSize  = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock      = DockStyle.Fill,
                Visible   = false,
                ForeColor = UiTheme.TextSecondary,
                BackColor = UiTheme.Surface,
                Font      = UiTheme.Body
            };

            Controls.Add(listView);
            Controls.Add(emptyLabel);
            Controls.Add(filterPanel);
            Controls.Add(UiTheme.MakeFormHeader("조직 자리비움 이력", null, "≡", UiTheme.Primary));

            orgCombo.SelectedIndexChanged += async (s, e) => await LoadUsersForSelectedOrgAsync();
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

                    bool isManager = mgr?.Success == true && ((mgr.Manager != null) || (mgr.Permissions != null && mgr.Permissions.Count > 0));
                    if (orgViewButton != null)
                    {
                        orgViewButton.Visible = isManager;
                    }

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
                    var orgs = ParseManagerOrgs(json);
                    if (orgs.Count > 0)
                    {
                        managerOrgs = orgs;
                        PopulateOrgComboFromManagerOrgs(managerOrgs);
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

                managerOrgs = list.Select(o => new OrgWithUsers { Code = o.Code, Name = o.Name }).ToList();
                PopulateOrgComboFromManagerOrgs(managerOrgs);
            }
            catch
            {
                orgCombo.Items.Clear();
                orgCombo.Items.Add(new ComboItem("전체", "ALL"));
                SetDefaultOrgSelection();
            }
        }

        private List<OrgWithUsers> ParseManagerOrgs(string json)
        {
            var list = new List<OrgWithUsers>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var source = root;
                if (root.ValueKind != JsonValueKind.Array && root.TryGetProperty("orgs", out var orgArr))
                {
                    source = orgArr;
                }

                foreach (var orgEl in EnumerateArrayLike(source))
                {
                    var code = NormalizeOrg(GetProp(orgEl, "code", "orgCode", "orgPath", "org", "path", "name"));
                    var name = GetProp(orgEl, "name", "orgName", "displayName");
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        code = name;
                    }
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = code;
                    }

                    var org = new OrgWithUsers
                    {
                        Code = NormalizeOrg(code),
                        Name = NormalizeOrg(name)
                    };

                    foreach (var userEl in EnumerateUsers(orgEl))
                    {
                        var id = GetProp(userEl, "employeeId", "empNo", "emp_no", "id", "userId", "empId");
                        if (string.IsNullOrWhiteSpace(id)) continue;

                        var displayName = GetProp(userEl, "displayName", "name", "employeeName", "empName", "emp_name", "userName");
                        var orgPath = ExtractOrgPath(userEl);
                        if (string.IsNullOrWhiteSpace(orgPath))
                        {
                            orgPath = org.Code;
                        }

                        org.Users.Add(new OrgUser
                        {
                            EmployeeId = id,
                            EmployeeName = string.IsNullOrWhiteSpace(displayName) ? id : displayName,
                            DisplayName = displayName,
                            OrgPath = orgPath,
                            Catcode = GetProp(userEl, "catcode"),
                            Catcode2 = GetProp(userEl, "catcode2"),
                            Catcode3 = GetProp(userEl, "catcode3")
                        });
                    }

                    list.Add(org);
                }
            }
            catch
            {
            }

            return list;
        }

        private static IEnumerable<JsonElement> EnumerateArrayLike(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                return element.EnumerateArray();
            }

            return element.ValueKind == JsonValueKind.Object ? new[] { element } : Enumerable.Empty<JsonElement>();
        }

        private static IEnumerable<JsonElement> EnumerateUsers(JsonElement orgElement)
        {
            string[] userKeys = new[] { "users", "members", "employees" };
            foreach (var key in userKeys)
            {
                if (orgElement.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        yield return item;
                    }
                }
            }
        }

        private void PopulateOrgComboFromManagerOrgs(List<OrgWithUsers> orgs)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<ComboItem>();

            void AddOrg(string path, string? display = null)
            {
                var normalized = NormalizeOrg(path);
                if (string.IsNullOrWhiteSpace(normalized) || seen.Contains(normalized)) return;
                seen.Add(normalized);
                items.Add(new ComboItem(string.IsNullOrWhiteSpace(display) ? normalized : display, normalized));
            }

            AddOrg("ALL", "전체");

            foreach (var org in orgs)
            {
                var code = string.IsNullOrWhiteSpace(org.Code) ? org.Name : org.Code;
                var normalized = NormalizeOrg(code);
                if (string.IsNullOrWhiteSpace(normalized)) continue;
       
                AddOrg(normalized, string.IsNullOrWhiteSpace(org.Name) ? normalized : org.Name);
            }

            orgCombo.Items.Clear();
            foreach (var item in items)
            {
                orgCombo.Items.Add(item);
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
            if (preferAllOrgOnFirstLoad)
            {
                preferAllOrgOnFirstLoad = false;
                for (int i = 0; i < orgCombo.Items.Count; i++)
                {
                    if (orgCombo.Items[i] is ComboItem allItem && string.Equals(allItem.Value, "ALL", StringComparison.OrdinalIgnoreCase))
                    {
                        orgCombo.SelectedIndex = i;
                        return;
                    }
                }
                orgCombo.SelectedIndex = orgCombo.Items.Count > 0 ? 0 : -1;
                return;
            }

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

            Dictionary<string, string> users = BuildUsersFromManagerOrgs(orgValue);
            var memberUsers = await FetchMembersByOrganizationAsync(orgValue);
            foreach (var kv in memberUsers)
            {
                users[kv.Key] = kv.Value;
            }
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
                    var parsed = ParseUsers(managerUsersJson, orgValue);
                    foreach (var kv in parsed)
                    {
                        users[kv.Key] = kv.Value;
                    }
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
            if (userCombo.Items.Count == 0)
             {
                 userCombo.Items.Add(new ComboItem("담당자 없음", string.Empty));
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

            var paths = new[] { "orgCode", "orgPath", "org", "orgName", "organization", "department", "departmentPath", "dept", "deptPath" }
                .Select(p => GetProp(item, p))
                .Where(v => !string.IsNullOrWhiteSpace(v));

            foreach (var path in paths)
            {
                if (IsOrgPrefix(path, orgValue))
                {
                    return true;
                }
            }

            var catcodeParts = new[] { GetProp(item, "catcode"), GetProp(item, "catcode2"), GetProp(item, "catcode3") }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (catcodeParts.Count > 0)
            {
                var combined = string.Join("/", catcodeParts);
                if (IsOrgPrefix(combined, orgValue))
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

        private async Task<Dictionary<string, string>> FetchMembersByOrganizationAsync(string orgValue)
        {
            var members = new Dictionary<string, string>();

            try
            {
                string url = $"{serverBaseUrl}/api/client/members";
#if DEBUG
                DebugLog("members 요청", url);
#endif
                using var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return members;
                }

                string json = await response.Content.ReadAsStringAsync();
#if DEBUG
                DebugLog("members 응답", json.Length > 4000 ? json.Substring(0, 4000) : json);
#endif
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                void AddIfMatches(JsonElement el)
                {
                    string id = GetProp(el, "id", "employeeId", "empNo", "emp_no", "userId", "empId");
                    string name = GetProp(el, "name", "displayName", "employeeName", "empName", "emp_name", "userName");

                    if (string.IsNullOrWhiteSpace(id) || !MatchesOrganization(el, orgValue))
                    {
                        return;
                    }

                    members[id] = string.IsNullOrWhiteSpace(name) ? id : name;
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        AddIfMatches(item);
                    }
                    return members;
                }

                string[] arrayKeys = new[] { "data", "items", "content", "members", "users", "employees" };
                foreach (var key in arrayKeys)
                {
                    if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            AddIfMatches(item);
                        }
                        return members;
                    }
                }

                foreach (var prop in root.EnumerateObject())
                {
                    var value = prop.Value;
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        AddIfMatches(value);
                    }
                    else if (value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in value.EnumerateArray())
                        {
                            AddIfMatches(item);
                        }
                    }
                }

                AddIfMatches(root);
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugLog("members 예외", ex.ToString());
#endif
            }

            return members;
        }

        private Dictionary<string, string> BuildUsersFromManagerOrgs(string orgValue)
        {
            var users = new Dictionary<string, string>();
            if (managerOrgs.Count == 0)
            {
                return users;
            }

            foreach (var org in managerOrgs)
            {
                if (!IsOrgPrefix(org.Code, orgValue) && !IsOrgPrefix(org.Name, orgValue))
                {
                    continue;
                }

                foreach (var user in org.Users)
                {
                    if (string.IsNullOrWhiteSpace(user.EmployeeId))
                    {
                        continue;
                    }

                    var path = string.IsNullOrWhiteSpace(user.OrgPath) ? org.Code : user.OrgPath!;
                    if (!IsOrgPrefix(path, orgValue))
                    {
                        continue;
                    }

                    var displayName = string.IsNullOrWhiteSpace(user.EmployeeName)
                        ? (string.IsNullOrWhiteSpace(user.DisplayName) ? user.EmployeeId : user.DisplayName!)
                        : user.EmployeeName;
                    users[user.EmployeeId] = displayName;
                }
            }

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
            return (value ?? string.Empty).Replace("\\", "/").Trim().Trim('/');
        }

        private static bool IsOrgPrefix(string? candidate, string orgValue)
        {
            var source = NormalizeOrg(candidate);
            var target = NormalizeOrg(orgValue);

            if (string.IsNullOrWhiteSpace(target) || string.Equals(target, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return source.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOrgMatch(string? source, string target)
        {
            return IsOrgPrefix(source, target) || IsOrgPrefix(target, source);
        }

        private void PopulateUserComboFromDict(Dictionary<string, string> users)
        {
            userCombo.Items.Clear();
            userCombo.Items.Add(new ComboItem("담당자 선택", string.Empty));
            foreach (var kv in users)
            {
                var name = string.IsNullOrWhiteSpace(kv.Value) ? kv.Key : kv.Value.Trim();
                userCombo.Items.Add(new ComboItem($"{name} ({kv.Key})", kv.Key));
            }
            if (userCombo.Items.Count > 0)
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

                RenderList(events);
            }
            catch
            {
                listView.Items.Clear();
                ShowEmptyState(true, "자리비움 이력을 불러오지 못했습니다.");
            }
        }

        private async Task LoadOrgIdleEventsAsync()
        {
            var selected = orgCombo.SelectedItem as ComboItem;
            string orgValue = selected?.Value ?? "ALL";
            
            var startDateKst = startPicker.Value.Date;
            var endDateKst = endPicker.Value.Date;

            try
            {
                string url = $"{serverBaseUrl}/api/client/manager-logs?employeeId={Uri.EscapeDataString(managerEmpId)}&startDate={startDateKst:yyyy-MM-dd}&endDate={endDateKst:yyyy-MM-dd}&orgPath={Uri.EscapeDataString(orgValue)}";
                string json = await httpClient.GetStringAsync(url);
                var events = ParseManagerLogsToIdle(json, orgValue);
                RenderList(events);
            }
            catch
            {
                ShowEmptyState(true, "조직 이력을 불러오지 못했습니다.");
            }
        }

        private List<IdleEvt> ParseManagerLogsToIdle(string json, string orgValue)
        {
            var list = new List<IdleEvt>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                IEnumerable<JsonElement> items;
                if (root.ValueKind == JsonValueKind.Array) items = root.EnumerateArray();
                else if (root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array) items = dataArr.EnumerateArray();
                else items = Enumerable.Empty<JsonElement>();

                foreach (var item in items)
                {
                    if (!MatchesOrganization(item, orgValue)) continue;
                    
                    var evt = new IdleEvt
                    {
                        EmployeeId = GetProp(item, "employeeId", "empNo", "emp_no", "id", "employee_id"),
                        EmployeeName = GetProp(item, "employeeName", "empName", "emp_name", "name", "displayName"),
                        IdleStartTime = GetProp(item, "idleStartTime", "idle_start_time", "startTime", "start_time"),
                        IdleEndTime = GetProp(item, "idleEndTime", "idle_end_time", "endTime", "end_time"),
                        ReasonDetail = GetProp(item, "reasonDetail", "reason_detail", "detail"),
                    };
                    list.Add(evt);
                }
            }
            catch { }
            return list;
        }

        private void RenderList(List<IdleEvt> events)
        {
            listView.Items.Clear();
            var startDateKst = startPicker.Value.Date;
            var endDateKst = endPicker.Value.Date;

            var filtered = events.Where(e => InRangeKst(e.IdleStartTime, e.IdleEndTime, startDateKst, endDateKst)).ToList();

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

            ShowEmptyState(filtered.Count == 0, filtered.Count == 0 ? "자리비움 이력이 없습니다." : "");
        }
        
        private void ShowEmptyState(bool show, string message)
        {
            emptyLabel.Text = message;
            emptyLabel.Visible = show;
            listView.Visible = !show;
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

        private class OrgWithUsers
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public List<OrgUser> Users { get; set; } = new();
        }

        private class OrgUser
        {
            public string EmployeeId { get; set; } = string.Empty;
            public string EmployeeName { get; set; } = string.Empty;
            public string? DisplayName { get; set; }
            public string? OrgPath { get; set; }
            public string Catcode { get; set; } = string.Empty;
            public string Catcode2 { get; set; } = string.Empty;
            public string Catcode3 { get; set; } = string.Empty;
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
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}