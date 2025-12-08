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
            ClientSize = new System.Drawing.Size(940, 520);
            StartPosition = FormStartPosition.CenterParent;

            dgvNotifications.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgvNotifications.Location = new System.Drawing.Point(12, 50);
            dgvNotifications.Size = new System.Drawing.Size(916, 420);
            dgvNotifications.ReadOnly = true;
            dgvNotifications.AllowUserToAddRows = false;
            dgvNotifications.AllowUserToDeleteRows = false;
            dgvNotifications.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvNotifications.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvNotifications.MultiSelect = false;
            dgvNotifications.DataBindingComplete += (s, e) => HideInternalColumns();
            dgvNotifications.SelectionChanged += (s, e) => UpdateButtons();

            btnRefresh.Text = "새로고침";
            btnRefresh.Location = new System.Drawing.Point(12, 12);
            btnRefresh.Click += async (s, e) => await RefreshNotificationsAsync();

            btnApprove.Text = "승인";
            btnApprove.Location = new System.Drawing.Point(100, 12);
            btnApprove.Click += async (s, e) => await UpdateSelectedStatusAsync("APPROVED");

            btnReject.Text = "반려";
            btnReject.Location = new System.Drawing.Point(188, 12);
            btnReject.Click += async (s, e) => await UpdateSelectedStatusAsync("REJECTED");

            btnClose.Text = "닫기";
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Location = new System.Drawing.Point(853, 12);
            btnClose.Click += (s, e) => Close();

            lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblStatus.Location = new System.Drawing.Point(560, 18);
            lblStatus.Size = new System.Drawing.Size(285, 15);

            Controls.AddRange(new Control[] { dgvNotifications, btnRefresh, btnApprove, btnReject, btnClose, lblStatus });
        }

        private async void ManagerNotificationListForm_Load(object? sender, EventArgs e)
        {
            if (initialNotificationsToMark.Count > 0)
            {
                await MarkNotificationsViewedAsync(initialNotificationsToMark);
            }

            await RefreshNotificationsAsync();
        }

        private async System.Threading.Tasks.Task RefreshNotificationsAsync()
        {
            btnRefresh.Enabled = false;
            lblStatus.Text = "불러오는 중...";

            try
            {
                var notifications = await FetchNotificationsAsync();
                var newIds = notifications.Where(n => string.Equals(n.NotificationStatus, "NEW", StringComparison.OrdinalIgnoreCase))
                                          .Select(n => n.NotificationId)
                                          .Where(id => !string.IsNullOrWhiteSpace(id))
                                          .ToList();
                if (newIds.Count > 0)
                {
                    await MarkNotificationsViewedAsync(newIds);
                }

                currentItems = new BindingList<ManagerNotificationRow>(notifications);
                dgvNotifications.DataSource = currentItems;

                // 목록이 있으면 첫 행을 자동 선택하여 버튼 활성화가 되도록 처리
                if (dgvNotifications.Rows.Count > 0)
                {
                    dgvNotifications.ClearSelection();
                    dgvNotifications.Rows[0].Selected = true;
                    dgvNotifications.CurrentCell = dgvNotifications.Rows[0].Cells[0];
                }

                lblStatus.Text = $"총 {notifications.Count}건";
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
                bool canModify = CanModifyRequest(row.RequestStatus);
                btnApprove.Enabled = hasValidRequest && canModify;
                btnReject.Enabled = hasValidRequest && canModify;
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
            if (!response.IsSuccessStatusCode)
            {
                string serverMessage = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"서버 응답: {response.StatusCode} {serverMessage}");
            }

            string json = await response.Content.ReadAsStringAsync();
            return ParseResponse(json);
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

                list.Add(new ManagerNotificationRow
                {
                    NotificationId = GetString(item, "id", "_id", "notificationId"),
                    NotificationStatus = GetString(item, "notificationStatus", "status", "state"),
                    RequestId = GetString(overtime, "id", "_id", "requestId", "request_id"),
                    EmployeeId = GetString(overtime, "employeeId", "employee_id", "empNo", "emp_no"),
                    EmployeeName = GetString(overtime, "employeeName", "employee_name", "empName", "emp_name"),
                    WorkDate = NormalizeDate(GetString(overtime, "workDate", "work_date", "date")),
                    StartTime = GetString(overtime, "startTime", "start_time", "start"),
                    EndTime = GetString(overtime, "endTime", "end_time", "end"),
                    Reason = GetString(overtime, "reason", "description", "comment"),
                    RequestStatus = GetString(overtime, "status", "approvalStatus", "approval_status", "result"),
                    SubmittedAt = NormalizeDateTime(GetString(overtime, "createdAt", "created_at", "submittedAt", "submitted_at")),
                });
            }

            return list;
        }

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

                MessageBox.Show("처리가 완료되었습니다.");
                await RefreshNotificationsAsync();
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
                MaximizeBox = false
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

            var result = form.ShowDialog(this);
            if (result != DialogResult.OK)
            {
                return null;
            }

            return textBox.Text.Trim();
        }

        private const string STATUS_APPROVED = "APPROVED";
        private const string STATUS_REJECTED = "REJECTED";

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

        [DisplayName("신청 상태")]
        public string RequestStatus { get; set; } = string.Empty;

        [DisplayName("알림 상태")]
        public string NotificationStatus { get; set; } = string.Empty;

        [DisplayName("신청 일시")]
        public string SubmittedAt { get; set; } = string.Empty;
    }
}