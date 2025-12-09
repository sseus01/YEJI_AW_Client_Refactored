using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class ManagerNotificationListForm : Form
    {
        private readonly string serverBaseUrl;
        private readonly HttpClient httpClient;
        private readonly string managerEmployeeId;
        private readonly string managerName;
        private readonly List<string> initialNotificationsToMark;

        private readonly DataGridView dgvNotifications = new();
        private readonly Button btnRefresh = new();
        private readonly Button btnApprove = new();
        private readonly Button btnReject = new();
        private readonly Button btnClose = new();
        private readonly Label lblStatus = new();
        private readonly Label lblStartDate = new();
        private readonly Label lblEndDate = new();
        private readonly DateTimePicker startPicker = new();
        private readonly DateTimePicker endPicker = new();

        private BindingList<ManagerNotificationRow> currentItems = new();

        public ManagerNotificationListForm(string serverBaseUrl, HttpClient httpClient, string managerEmployeeId, string managerName, IEnumerable<string>? notificationIdsToMark)
        {
            this.serverBaseUrl = serverBaseUrl;
            this.httpClient = httpClient;
            this.managerEmployeeId = managerEmployeeId;
            this.managerName = managerName;
            this.initialNotificationsToMark = notificationIdsToMark?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? new List<string>();

            BuildLayout();

            Load += ManagerNotificationListForm_Load;
        }

        private void BuildLayout()
        {
            Text = "연장 근무 승인 요청";
            ClientSize = new System.Drawing.Size(940, 560);
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            // 애플리케이션 아이콘 설정
            try
            {
                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrayIcon_Y_orange.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    Icon = new System.Drawing.Icon(iconPath);
                }
            }
            catch { /* 아이콘 로드 실패 시 무시 */ }

            // 조회 기간 선택 UI
            lblStartDate.Text = "시작일";
            lblStartDate.AutoSize = true;
            lblStartDate.Location = new System.Drawing.Point(12, 16);

            startPicker.Format = DateTimePickerFormat.Custom;
            startPicker.CustomFormat = "yyyy-MM-dd";
            startPicker.Width = 110;
            startPicker.Location = new System.Drawing.Point(56, 12);
            startPicker.Value = DateTime.Today.AddDays(-7);

            lblEndDate.Text = "종료일";
            lblEndDate.AutoSize = true;
            lblEndDate.Location = new System.Drawing.Point(176, 16);

            endPicker.Format = DateTimePickerFormat.Custom;
            endPicker.CustomFormat = "yyyy-MM-dd";
            endPicker.Width = 110;
            endPicker.Location = new System.Drawing.Point(220, 12);
            endPicker.Value = DateTime.Today;

            btnRefresh.Text = "조회";
            btnRefresh.Location = new System.Drawing.Point(340, 11);
            btnRefresh.Click += async (s, e) => await RefreshNotificationsAsync();

            btnApprove.Text = "승인";
            btnApprove.Location = new System.Drawing.Point(420, 12);
            btnApprove.Click += async (s, e) => await UpdateSelectedStatusAsync("APPROVED");

            btnReject.Text = "반려";
            btnReject.Location = new System.Drawing.Point(500, 12);
            btnReject.Click += async (s, e) => await UpdateSelectedStatusAsync("REJECTED");

            btnClose.Text = "닫기";
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Location = new System.Drawing.Point(853, 12);
            btnClose.Click += (s, e) => Close();

            lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblStatus.Location = new System.Drawing.Point(560, 18);
            lblStatus.Size = new System.Drawing.Size(285, 15);

            dgvNotifications.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgvNotifications.Location = new System.Drawing.Point(12, 50);
            dgvNotifications.Size = new System.Drawing.Size(916, 490);
            dgvNotifications.ReadOnly = true;
            dgvNotifications.AllowUserToAddRows = false;
            dgvNotifications.AllowUserToDeleteRows = false;
            dgvNotifications.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvNotifications.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvNotifications.MultiSelect = false;
            dgvNotifications.DataBindingComplete += (s, e) =>
            {
                HideInternalColumns();
                if (dgvNotifications.Rows.Count > 0)
                {
                    dgvNotifications.ClearSelection();
                    dgvNotifications.Rows[0].Selected = true;
                    dgvNotifications.CurrentCell = dgvNotifications.Rows[0].Cells[0];
                }
                UpdateButtons();
            };
            dgvNotifications.SelectionChanged += (s, e) => UpdateButtons();

            Controls.AddRange(new Control[] { lblStartDate, startPicker, lblEndDate, endPicker, btnRefresh, btnApprove, btnReject, btnClose, lblStatus, dgvNotifications });
        }

        private async void ManagerNotificationListForm_Load(object? sender, EventArgs e)
        {
            // Position the form above the system tray
            PositionFormNearTray();

            if (initialNotificationsToMark.Count > 0)
            {
                await MarkNotificationsViewedAsync(initialNotificationsToMark);
            }

            await RefreshNotificationsAsync();
        }

        private void PositionFormNearTray()
        {
            // Get the working area of the screen (excluding taskbar)
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;

            // Position form in bottom-right corner, just above the taskbar
            int x = workingArea.Right - Width - 10; // 10px margin from right edge
            int y = workingArea.Bottom - Height - 10; // 10px margin from bottom edge

            // Ensure the form stays within screen bounds
            if (x < workingArea.Left) x = workingArea.Left;
            if (y < workingArea.Top) y = workingArea.Top;

            Location = new System.Drawing.Point(x, y);
        }

        private async System.Threading.Tasks.Task RefreshNotificationsAsync()
        {
            btnRefresh.Enabled = false;
            lblStatus.Text = "불러오는 중...";

            try
            {
                var notifications = await FetchNotificationsAsync();

                // 조회 기간 필터 적용 (WorkDate 기반)
                DateTime from = startPicker.Value.Date;
                DateTime to = endPicker.Value.Date;
                var filtered = notifications.Where(n =>
                {
                    if (DateTime.TryParse(n.WorkDate, out var d))
                    {
                        return d.Date >= from && d.Date <= to;
                    }
                    return true; // 파싱 실패 시 표시
                }).ToList();

                var newIds = filtered.Where(n => string.Equals(n.NotificationStatus, "NEW", StringComparison.OrdinalIgnoreCase))
                                      .Select(n => n.NotificationId)
                                      .Where(id => !string.IsNullOrWhiteSpace(id))
                                      .ToList();
                if (newIds.Count > 0)
                {
                    await MarkNotificationsViewedAsync(newIds);
                }

                // Sort by SubmittedAt in descending order (newest first)
                filtered.Sort((a, b) =>
                {
                    if (DateTime.TryParse(a.SubmittedAt, out var dateA) && DateTime.TryParse(b.SubmittedAt, out var dateB))
                    {
                        return dateB.CompareTo(dateA); // Descending order
                    }
                    return string.Compare(b.SubmittedAt, a.SubmittedAt, StringComparison.Ordinal);
                });

                currentItems = new BindingList<ManagerNotificationRow>(filtered);
                dgvNotifications.DataSource = currentItems;

                if (dgvNotifications.Rows.Count > 0)
                {
                    dgvNotifications.ClearSelection();
                    dgvNotifications.Rows[0].Selected = true;
                    dgvNotifications.CurrentCell = dgvNotifications.Rows[0].Cells[0];
                }

                lblStatus.Text = $"총 {filtered.Count}건";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"알림 목록을 불러오는데 실패했습니다.\n{ex.Message}");
                lblStatus.Text = "오류 발생";
            }
            finally
            {
                btnRefresh.Enabled = true;
                UpdateButtons();
            }
        }

        private void UpdateButtons()
        {
            if (dgvNotifications.CurrentRow?.DataBoundItem is ManagerNotificationRow row)
            {
                bool hasValidRequest = !string.IsNullOrWhiteSpace(row.RequestId);
                string status = (row.RawRequestStatus ?? string.Empty).Trim().ToUpperInvariant();
                bool approveEnabled = hasValidRequest && (status == "PENDING" || status == "REJECTED" || string.IsNullOrWhiteSpace(status));
                bool rejectEnabled = hasValidRequest && (status == "PENDING" || status == "APPROVED" || string.IsNullOrWhiteSpace(status));
                btnApprove.Enabled = approveEnabled;
                btnReject.Enabled = rejectEnabled;
            }
            else
            {
                btnApprove.Enabled = false;
                btnReject.Enabled = false;
            }
        }

        private void HideInternalColumns()
        {
            if (dgvNotifications.Columns[nameof(ManagerNotificationRow.NotificationId)] != null)
            {
                dgvNotifications.Columns[nameof(ManagerNotificationRow.NotificationId)].Visible = false;
            }

            if (dgvNotifications.Columns[nameof(ManagerNotificationRow.RequestId)] != null)
            {
                dgvNotifications.Columns[nameof(ManagerNotificationRow.RequestId)].Visible = false;
            }
        }

        private async System.Threading.Tasks.Task<List<ManagerNotificationRow>> FetchNotificationsAsync()
        {
            string url = $"{serverBaseUrl}/api/client/manager-notifications?employeeId={Uri.EscapeDataString(managerEmployeeId)}";
            using var response = await httpClient.GetAsync(url);
            var list = new List<ManagerNotificationRow>();
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                list = ParseResponse(json);
            }

            // 선택한 기간의 연장근무 요청을 병합 (승인/반려 포함하여 전체 표시)
            var rangeRequests = await FetchRequestsForRangeAsync(startPicker.Value.Date, endPicker.Value.Date);
            list = MergeRequests(list, rangeRequests);
            return list;
        }

        private async System.Threading.Tasks.Task<List<ManagerNotificationRow>> FetchRequestsForRangeAsync(DateTime fromDate, DateTime toDate)
        {
            var list = new List<ManagerNotificationRow>();
            try
            {
                string url = $"{serverBaseUrl}/api/overtime-requests?startDate={fromDate:yyyy-MM-dd}&endDate={toDate:yyyy-MM-dd}&managerId={Uri.EscapeDataString(managerEmployeeId)}";
                using var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return list;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                IEnumerable<JsonElement> Enumerate(JsonElement el)
                {
                    if (el.ValueKind == JsonValueKind.Array) return el.EnumerateArray();
                    if (el.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array) return dataEl.EnumerateArray();
                    if (el.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array) return itemsEl.EnumerateArray();
                    return Array.Empty<JsonElement>();
                }

                string NormalizeDate(string value) => DateTime.TryParse(value, out var dt) ? dt.ToString("yyyy-MM-dd") : value;
                string NormalizeDateTime(string value) => DateTime.TryParse(value, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : value;

                foreach (var e in Enumerate(root))
                {
                    var row = new ManagerNotificationRow
                    {
                        NotificationId = string.Empty,
                        NotificationStatus = string.Empty,
                        RequestId = GetString(e, "id", "requestId", "request_id", "reqId", "req_id"),
                        EmployeeId = GetString(e, "employeeId", "employee_id", "empNo", "emp_no"),
                        EmployeeName = GetString(e, "employeeName", "employee_name", "empName", "emp_name"),
                        WorkDate = NormalizeDate(GetString(e, "workDate", "work_date", "date")),
                        StartTime = GetString(e, "startTime", "start_time", "start"),
                        EndTime = GetString(e, "endTime", "end_time", "end"),
                        Reason = GetString(e, "reason", "description", "comment"),
                        RawRequestStatus = GetString(e, "status", "approvalStatus", "approval_status", "result"),
                        RequestStatus = TranslateStatus(GetString(e, "status", "approvalStatus", "approval_status", "result")),
                        SubmittedAt = NormalizeDateTime(GetString(e, "createdAt", "created_at", "submittedAt", "submitted_at")),
                    };
                    if (!string.IsNullOrWhiteSpace(row.RequestId))
                    {
                        list.Add(row);
                    }
                }
            }
            catch
            {
                // 무시
            }
            return list;
        }

        private List<ManagerNotificationRow> MergeRequests(List<ManagerNotificationRow> notifications, List<ManagerNotificationRow> todayRequests)
        {
            var byReq = notifications.ToDictionary(n => n.RequestId ?? string.Empty, n => n);
            foreach (var r in todayRequests)
            {
                if (string.IsNullOrWhiteSpace(r.RequestId)) continue;
                if (byReq.TryGetValue(r.RequestId, out var existing))
                {
                    existing.RawRequestStatus = string.IsNullOrWhiteSpace(r.RawRequestStatus) ? existing.RawRequestStatus : r.RawRequestStatus;
                    existing.RequestStatus = string.IsNullOrWhiteSpace(r.RequestStatus) ? existing.RequestStatus : r.RequestStatus;
                    existing.Reason = string.IsNullOrWhiteSpace(r.Reason) ? existing.Reason : r.Reason;
                    existing.StartTime = string.IsNullOrWhiteSpace(r.StartTime) ? existing.StartTime : r.StartTime;
                    existing.EndTime = string.IsNullOrWhiteSpace(r.EndTime) ? existing.EndTime : r.EndTime;
                    // 이름/사번이 비어있는 경우 요청 데이터로 보강
                    if (string.IsNullOrWhiteSpace(existing.EmployeeName) && !string.IsNullOrWhiteSpace(r.EmployeeName))
                    {
                        existing.EmployeeName = r.EmployeeName;
                    }
                    if (string.IsNullOrWhiteSpace(existing.EmployeeId) && !string.IsNullOrWhiteSpace(r.EmployeeId))
                    {
                        existing.EmployeeId = r.EmployeeId;
                    }
                }
                else
                {
                    byReq[r.RequestId] = r;
                }
            }
            return byReq.Values.ToList();
        }

        private async System.Threading.Tasks.Task<List<ManagerNotificationRow>> FetchTodayRequestsAsync()
        {
            var list = new List<ManagerNotificationRow>();
            try
            {
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                string url = $"{serverBaseUrl}/api/overtime-requests?startDate={today}&endDate={today}&managerId={Uri.EscapeDataString(managerEmployeeId)}";
                using var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return list;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                IEnumerable<JsonElement> Enumerate(JsonElement el)
                {
                    if (el.ValueKind == JsonValueKind.Array) return el.EnumerateArray();
                    if (el.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array) return dataEl.EnumerateArray();
                    if (el.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array) return itemsEl.EnumerateArray();
                    return Array.Empty<JsonElement>();
                }

                string NormalizeDate(string value)
                {
                    return DateTime.TryParse(value, out var dt) ? dt.ToString("yyyy-MM-dd") : value;
                }
                string NormalizeDateTime(string value)
                {
                    return DateTime.TryParse(value, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : value;
                }

                foreach (var e in Enumerate(root))
                {
                    var row = new ManagerNotificationRow
                    {
                        NotificationId = string.Empty,
                        NotificationStatus = string.Empty,
                        RequestId = GetString(e, "id", "requestId", "request_id", "reqId", "req_id"),
                        EmployeeId = GetString(e, "employeeId", "employee_id", "empNo", "emp_no"),
                        EmployeeName = GetString(e, "employeeName", "employee_name", "empName", "emp_name"),
                        WorkDate = NormalizeDate(GetString(e, "workDate", "work_date", "date")),
                        StartTime = GetString(e, "startTime", "start_time", "start"),
                        EndTime = GetString(e, "endTime", "end_time", "end"),
                        Reason = GetString(e, "reason", "description", "comment"),
                        RawRequestStatus = GetString(e, "status", "approvalStatus", "approval_status", "result"),
                        RequestStatus = TranslateStatus(GetString(e, "status", "approvalStatus", "approval_status", "result")),
                        SubmittedAt = NormalizeDateTime(GetString(e, "createdAt", "created_at", "submittedAt", "submitted_at")),
                    };
                    if (!string.IsNullOrWhiteSpace(row.RequestId))
                    {
                        list.Add(row);
                    }
                }
            }
            catch
            {
                // 무시
            }
            return list;
        }

        private List<ManagerNotificationRow> ParseResponse(string json)
        {
            var list = new List<ManagerNotificationRow>();

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

                string NormalizeDate(string value)
                {
                    if (DateTime.TryParse(value, out var dt))
                    {
                        return dt.ToString("yyyy-MM-dd");
                    }
                    return value;
                }

                string NormalizeDateTime(string value)
                {
                    if (DateTime.TryParse(value, out var dt))
                    {
                        return dt.ToString("yyyy-MM-dd HH:mm");
                    }
                    return value;
                }

                string rawStatus = GetString(overtime, "status", "approvalStatus", "approval_status", "result");

                // RequestId 우선 후보: 중첩 객체(overtime)에서 id 파싱
                var parsedRequestId = GetString(
                    overtime,
                    "id", "_id", "requestId", "request_id", "reqId", "req_id", "overtimeRequestId", "overtime_request_id");
                // fallback: 루트(알림 객체)에서도 다양한 필드명으로 시도
                if (string.IsNullOrWhiteSpace(parsedRequestId))
                {
                    parsedRequestId = GetString(
                        item,
                        "overtimeRequestId", "overtime_request_id",
                        "requestId", "request_id", "reqId", "req_id");
                }

                list.Add(new ManagerNotificationRow
                {
                    NotificationId = GetString(item, "id", "_id", "notificationId"),
                    NotificationStatus = GetString(item, "notificationStatus", "status", "state"),
                    RequestId = parsedRequestId,
                    EmployeeId = GetString(overtime, "employeeId", "employee_id", "empNo", "emp_no"),
                    EmployeeName = GetString(overtime, "employeeName", "employee_name", "empName", "emp_name"),
                    WorkDate = NormalizeDate(GetString(overtime, "workDate", "work_date", "date")),
                    StartTime = GetString(overtime, "startTime", "start_time", "start"),
                    EndTime = GetString(overtime, "endTime", "end_time", "end"),
                    Reason = GetString(overtime, "reason", "description", "comment"),
                    RawRequestStatus = rawStatus,
                    RequestStatus = TranslateStatus(rawStatus),
                    SubmittedAt = NormalizeDateTime(GetString(overtime, "createdAt", "created_at", "submittedAt", "submitted_at")),
                });
            }

            return list;
        }

        private static string TranslateStatus(string rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus)) return string.Empty;
            string normalized = rawStatus.Trim().ToUpperInvariant();
            return normalized switch
            {
                "PENDING" => "승인대기",
                "APPROVED" => "승인",
                "REJECTED" => "반려",
                _ => rawStatus
            };
        }

        private static bool CanModifyRequest(string requestStatus)
        {
            if (string.IsNullOrWhiteSpace(requestStatus))
            {
                // 상태가 없으면 수정 가능한 것으로 간주 (기본값)
                // 서버에서 상태가 아직 설정되지 않았거나, API 응답 형식이 다를 수 있음
                // 실제 승인/반려 시 서버에서 권한 검증이 이루어지므로 UX를 위해 버튼 활성화
                return true;
            }

            string normalized = requestStatus.Trim().ToUpperInvariant();
            // 이미 승인되었거나 반려된 요청은 수정 불가
            return normalized != STATUS_APPROVED && normalized != STATUS_REJECTED;
        }

        private async System.Threading.Tasks.Task MarkNotificationsViewedAsync(IEnumerable<string> ids)
        {
            foreach (var id in ids.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                string url = $"{serverBaseUrl}/api/client/manager-notifications/{Uri.EscapeDataString(id)}/viewed?employeeId={Uri.EscapeDataString(managerEmployeeId)}";
                try
                {
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                    await httpClient.SendAsync(request);
                }
                catch
                {
                    // 읽음 처리 실패는 조용히 무시 (다음 폴링에서 재시도)
                }
            }
        }

        private async System.Threading.Tasks.Task UpdateSelectedStatusAsync(string status)
        {
            if (dgvNotifications.CurrentRow?.DataBoundItem is not ManagerNotificationRow row || string.IsNullOrWhiteSpace(row.RequestId))
            {
                MessageBox.Show("처리할 신청을 선택해주세요.");
                return;
            }

            string? rejectionReason = null;
            if (string.Equals(status, "REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                rejectionReason = PromptForComment();
                if (rejectionReason == null)
                {
                    return;
                }
            }

            await UpdateStatusAsync(row.RequestId, status, rejectionReason);
        }

        private async System.Threading.Tasks.Task UpdateStatusAsync(string requestId, string status, string? comment)
        {
            btnApprove.Enabled = false;
            btnReject.Enabled = false;

            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["status"] = status,
                    ["approvedBy"] = managerName,
                    ["employeeId"] = managerEmployeeId,
                };

                if (!string.IsNullOrWhiteSpace(comment))
                {
                    payload["comment"] = comment;
                }

                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await httpClient.PutAsync($"{serverBaseUrl}/api/overtime-requests/{Uri.EscapeDataString(requestId)}/status", content);
                if (!response.IsSuccessStatusCode)
                {
                    string message = await response.Content.ReadAsStringAsync();
                    MessageBox.Show($"처리에 실패했습니다.\n{response.StatusCode}\n{message}");
                    return;
                }

                // 성공 시 현재 목록에서 해당 행을 즉시 업데이트하여 리스트에서 사라지지 않도록 처리
                var target = currentItems.FirstOrDefault(x => string.Equals(x.RequestId, requestId, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    target.RawRequestStatus = status;
                    target.RequestStatus = TranslateStatus(status);
                    // 승인/반려 후 이름이 빈칸으로 표시되는 문제 방지: 이름/사번이 비었으면 그대로 유지
                    if (string.IsNullOrWhiteSpace(target.EmployeeName))
                    {
                        target.EmployeeName = target.EmployeeName; // no-op to emphasize keep
                    }
                    if (string.IsNullOrWhiteSpace(target.EmployeeId))
                    {
                        target.EmployeeId = target.EmployeeId; // no-op
                    }
                }
                dgvNotifications.Refresh();

                MessageBox.Show("처리가 완료되었습니다.");
                // 전체 새로고침은 생략하여 항목이 사라지지 않게 함
                // await RefreshNotificationsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"상태 변경 중 오류가 발생했습니다.\n{ex.Message}");
            }
            finally
            {
                UpdateButtons();
            }
        }

        private string? PromptForComment()
        {
            using var form = new Form
            {
                Text = "반려 사유 입력 (선택)",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new System.Drawing.Size(380, 160),
                MinimizeBox = false,
                MaximizeBox = false,
                TopMost = true,               // 리스트창보다 위에 표시
                ShowInTaskbar = false
            };

            var label = new Label
            {
                Text = "반려 사유를 입력하세요. (미입력 가능)",
                AutoSize = true,
                Location = new System.Drawing.Point(12, 15)
            };

            var textBox = new TextBox
            {
                Location = new System.Drawing.Point(12, 40),
                Width = 350,
                Height = 60,
                Multiline = true
            };

            var btnOk = new Button
            {
                Text = "확인",
                DialogResult = DialogResult.OK,
                Location = new System.Drawing.Point(206, 110)
            };

            var btnCancel = new Button
            {
                Text = "취소",
                DialogResult = DialogResult.Cancel,
                Location = new System.Drawing.Point(287, 110)
            };

            form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;

            // 부모 활성화 후 다이얼로그를 맨 위로 띄움
            this.Activate();
            form.BringToFront();

            var result = form.ShowDialog(this);
            if (result != DialogResult.OK)
            {
                return null;
            }

            return textBox.Text.Trim();
        }

        private const string STATUS_APPROVED = "APPROVED";
        private const string STATUS_REJECTED = "REJECTED";

        private static string GetString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null)
                {
                    if (prop.ValueKind == JsonValueKind.String)
                        return prop.GetString() ?? string.Empty;
                    return prop.ToString();
                }
            }
            return string.Empty;
        }

        private static IEnumerable<JsonElement> EnumerateArrayLike(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    yield return child;
                }
                yield break;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in dataProp.EnumerateArray())
                    {
                        yield return child;
                    }
                }
            }
        }
    }

    public class ManagerNotificationRow
    {
        [Browsable(false)]
        public string NotificationId { get; set; } = string.Empty;

        [Browsable(false)]
        public string RequestId { get; set; } = string.Empty;

        [DisplayName("신청자 사번")]
        public string EmployeeId { get; set; } = string.Empty;

        [DisplayName("신청자 이름")]
        public string EmployeeName { get; set; } = string.Empty;

        [DisplayName("근무일")]
        public string WorkDate { get; set; } = string.Empty;

        [DisplayName("시작")]
        public string StartTime { get; set; } = string.Empty;

        [DisplayName("종료")]
        public string EndTime { get; set; } = string.Empty;

        [DisplayName("사유")]
        public string Reason { get; set; } = string.Empty;

        [Browsable(false)]
        public string RawRequestStatus { get; set; } = string.Empty;

        [DisplayName("신청 상태")]
        public string RequestStatus { get; set; } = string.Empty;

        [DisplayName("알림 상태")]
        public string NotificationStatus { get; set; } = string.Empty;

        [DisplayName("신청 일시")]
        public string SubmittedAt { get; set; } = string.Empty;
    }
}