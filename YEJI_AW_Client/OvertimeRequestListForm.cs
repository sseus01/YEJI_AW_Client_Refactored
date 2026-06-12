using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class OvertimeRequestListForm : Form
    {
        private readonly string serverBaseUrl;
        private readonly System.Net.Http.HttpClient httpClient;
        private readonly string employeeId;

        private Label lblStartDate = new Label();
        private DateTimePicker dtpStartDate = new DateTimePicker();
        private Label lblEndDate = new Label();
        private DateTimePicker dtpEndDate = new DateTimePicker();
        private RoundButton btnSearch = new RoundButton();
        private RoundButton btnDelete = new RoundButton();
        private DataGridView dgvRequests = new DataGridView();
        private Label lblStatus = new Label();

        private BindingList<OvertimeRequestEntry> currentItems = new BindingList<OvertimeRequestEntry>();

        public OvertimeRequestListForm(string serverBaseUrl, System.Net.Http.HttpClient httpClient, string employeeId)
        {
            this.serverBaseUrl = serverBaseUrl;
            this.httpClient = httpClient;
            this.employeeId = employeeId;

            BuildLayout();

            Load += OvertimeRequestListForm_Load;
        }

        private void BuildLayout()
        {
            Text          = "연장근무신청 확인";
            ClientSize    = new Size(820, 520);
            StartPosition = FormStartPosition.Manual;
            BackColor     = UiTheme.Background;

            // ── 필터 바 ─────────────────────────────────────────────
            var filterPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 46,
                BackColor = UiTheme.Surface
            };

            lblStartDate.Text      = "시작일";
            lblStartDate.AutoSize  = true;
            lblStartDate.Font      = UiTheme.Small;
            lblStartDate.ForeColor = UiTheme.TextSecondary;
            lblStartDate.Location  = new Point(12, 14);

            dtpStartDate.Format       = DateTimePickerFormat.Custom;
            dtpStartDate.CustomFormat = "yyyy-MM-dd";
            dtpStartDate.Location     = new Point(62, 11);
            dtpStartDate.Size         = new Size(110, 24);
            dtpStartDate.Value        = DateTime.Today;

            lblEndDate.Text      = "종료일";
            lblEndDate.AutoSize  = true;
            lblEndDate.Font      = UiTheme.Small;
            lblEndDate.ForeColor = UiTheme.TextSecondary;
            lblEndDate.Location  = new Point(182, 14);

            dtpEndDate.Format       = DateTimePickerFormat.Custom;
            dtpEndDate.CustomFormat = "yyyy-MM-dd";
            dtpEndDate.Location     = new Point(232, 11);
            dtpEndDate.Size         = new Size(110, 24);
            dtpEndDate.Value        = DateTime.Today;

            btnSearch.Text     = "조회";
            btnSearch.Location = new Point(352, 6);
            btnSearch.Size     = new Size(70, UiTheme.BtnH);
            UiTheme.StylePrimary(btnSearch);
            btnSearch.Click += BtnSearch_Click;

            btnDelete.Text     = "취소";
            btnDelete.Location = new Point(428, 6);
            btnDelete.Size     = new Size(70, UiTheme.BtnH);
            UiTheme.StyleDanger(btnDelete);
            btnDelete.Enabled = false;
            btnDelete.Click += BtnDelete_Click;

            lblStatus.AutoSize  = false;
            lblStatus.TextAlign = ContentAlignment.MiddleRight;
            lblStatus.Anchor    = AnchorStyles.Top | AnchorStyles.Right;
            lblStatus.Font      = UiTheme.Small;
            lblStatus.ForeColor = UiTheme.TextSecondary;
            lblStatus.Location  = new Point(550, 14);
            lblStatus.Size      = new Size(258, 18);

            filterPanel.Controls.AddRange(new Control[]
            {
                lblStartDate, dtpStartDate, lblEndDate, dtpEndDate, btnSearch, btnDelete, lblStatus
            });

            // ── DataGridView ────────────────────────────────────────
            dgvRequests.Dock                 = DockStyle.Fill;
            dgvRequests.AutoSizeColumnsMode  = DataGridViewAutoSizeColumnsMode.Fill;
            dgvRequests.MultiSelect          = false;
            dgvRequests.DataBindingComplete += DgvRequests_DataBindingComplete;
            dgvRequests.SelectionChanged    += DgvRequests_SelectionChanged;
            UiTheme.StyleDataGridView(dgvRequests);

            Controls.Add(dgvRequests);
            Controls.Add(filterPanel);
            Controls.Add(UiTheme.MakeFormHeader("연장근무신청 확인", null, "≡", UiTheme.Primary));
        }

        private async void OvertimeRequestListForm_Load(object? sender, EventArgs e)
        {
            // Position the form above the system tray
            PositionFormNearTray();
            await RefreshListAsync();
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

            Location = new Point(x, y);
        }

        private async void BtnSearch_Click(object? sender, EventArgs e)
        {
            await RefreshListAsync();
        }

        private async System.Threading.Tasks.Task RefreshListAsync()
        {
            btnSearch.Enabled = false;
            lblStatus.Text = "조회 중...";

            try
            {
                var urlBuilder = new StringBuilder($"{serverBaseUrl}/api/overtime-requests?employeeId={Uri.EscapeDataString(employeeId)}");

                // 날짜 피커 값 항상 포함
                urlBuilder.Append($"&startDate={Uri.EscapeDataString(dtpStartDate.Value.ToString("yyyy-MM-dd"))}");
                urlBuilder.Append($"&endDate={Uri.EscapeDataString(dtpEndDate.Value.ToString("yyyy-MM-dd"))}");

                using var response = await httpClient.GetAsync(urlBuilder.ToString());
                if (!response.IsSuccessStatusCode)
                {
                    string message = await response.Content.ReadAsStringAsync();
                    ShowErrorMessage(response.StatusCode, message);
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var items = ParseResponse(json, jsonOptions);
                // Sort by SubmittedAt in descending order (newest first)
                items.Sort((a, b) =>
                {
                    if (DateTime.TryParse(a.SubmittedAt, out var dateA) && DateTime.TryParse(b.SubmittedAt, out var dateB))
                    {
                        return dateB.CompareTo(dateA); // Descending order
                    }
                    return string.Compare(b.SubmittedAt, a.SubmittedAt, StringComparison.Ordinal);
                });
                currentItems = new BindingList<OvertimeRequestEntry>(items);
                dgvRequests.AutoGenerateColumns = true;
                dgvRequests.DataSource = currentItems;
                lblStatus.Text = $"총 {items.Count}건";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"연장 근무 신청 이력을 불러오는데 실패했습니다.\n{ex.Message}");
                lblStatus.Text = "오류 발생";
            }
            finally
            {
                btnSearch.Enabled = true;
                UpdateDeleteButtonState();
            }
        }

        private void DgvRequests_SelectionChanged(object? sender, EventArgs e)
        {
            UpdateDeleteButtonState();
        }

        private void DgvRequests_DataBindingComplete(object? sender, DataGridViewBindingCompleteEventArgs e)
        {
            if (dgvRequests.Columns[nameof(OvertimeRequestEntry.Id)] != null)
            {
                dgvRequests.Columns[nameof(OvertimeRequestEntry.Id)].Visible = false;
            }

            if (dgvRequests.Columns[nameof(OvertimeRequestEntry.RawStatus)] != null)
            {
                dgvRequests.Columns[nameof(OvertimeRequestEntry.RawStatus)].Visible = false;
            }

            UpdateDeleteButtonState();
        }

        private void UpdateDeleteButtonState()
        {
            if (dgvRequests.CurrentRow?.DataBoundItem is OvertimeRequestEntry entry)
            {
                btnDelete.Enabled = IsPending(entry.RawStatus) && !string.IsNullOrWhiteSpace(entry.Id);
            }
            else
            {
                btnDelete.Enabled = false;
            }
        }

        private async void BtnDelete_Click(object? sender, EventArgs e)
        {
            await DeleteSelectedAsync();
        }

        private async System.Threading.Tasks.Task DeleteSelectedAsync()
        {
            if (dgvRequests.CurrentRow?.DataBoundItem is not OvertimeRequestEntry entry)
            {
                MessageBox.Show("취소할 신청을 선택해주세요.");
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                MessageBox.Show("선택한 신청 건의 식별자를 찾을 수 없습니다. 관리자에게 문의하세요.");
                return;
            }

            if (!IsPending(entry.RawStatus))
            {
                MessageBox.Show("접수 중 상태의 신청만 취소할 수 있습니다.");
                return;
            }

            var confirm = MessageBox.Show("선택한 신청을 취소하시겠습니까?", "취소 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            btnDelete.Enabled = false;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Delete,
                     $"{serverBaseUrl}/api/overtime-requests/{Uri.EscapeDataString(entry.Id)}")
                {
                     Content = new StringContent(
                        JsonSerializer.Serialize(new { employeeId }),
                        Encoding.UTF8,
                        "application/json")
                    };

                using var response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string message = await response.Content.ReadAsStringAsync();
                    ShowErrorMessage(response.StatusCode, message);
                    return;
                }

                MessageBox.Show("신청 건이 취소되었습니다.");
                await RefreshListAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"취소 중 오류가 발생했습니다.\\n{ex.Message}");
            }
            finally
            {
                UpdateDeleteButtonState();
            }
        }

        private void ShowErrorMessage(System.Net.HttpStatusCode statusCode, string serverMessage)
        {
            string message = statusCode switch
            {
                System.Net.HttpStatusCode.BadRequest => "요청 값이 올바르지 않습니다. 입력한 값을 확인해주세요.",
                System.Net.HttpStatusCode.NotFound => "대상 데이터를 찾을 수 없습니다.",
                _ => "서버 처리 중 문제가 발생했습니다."
            };

            if (!string.IsNullOrWhiteSpace(serverMessage))
            {
                message += $"\n서버 메시지: {serverMessage}";
            }

            MessageBox.Show(message, "조회 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            lblStatus.Text = "조회 실패";
        }

        private static List<OvertimeRequestEntry> ParseResponse(string json, JsonSerializerOptions jsonOptions)
        {
            string preview = json.Length > 300 ? json.Substring(0, 300) + "..." : json;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    return MapEntries(root.EnumerateArray());
                }

                if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    return MapEntries(dataElement.EnumerateArray());
                }

                if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                {
                    return MapEntries(itemsElement.EnumerateArray());
                }

                if (root.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.Array)
                {
                    return MapEntries(contentElement.EnumerateArray());
                }
            }
            catch (JsonException)
            {
                // fallthrough to throw below
            }

            throw new InvalidOperationException($"서버 응답을 처리할 수 없습니다. 관리자에게 문의하세요. 응답 미리보기: {preview}");
        }

        private static List<OvertimeRequestEntry> MapEntries(IEnumerable<JsonElement> elements)
        {
            var list = new List<OvertimeRequestEntry>();
            foreach (var element in elements)
            {
                list.Add(MapElement(element));
            }
            return list;
        }

        private static OvertimeRequestEntry MapElement(JsonElement element)
        {
            string GetString(params string[] names)
            {
                foreach (var name in names)
                {
                    if (element.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null)
                    {
                        if (prop.ValueKind == JsonValueKind.String)
                        {
                            return prop.GetString() ?? string.Empty;
                        }
                        return prop.ToString();
                    }
                }
                return string.Empty;
            }

            string NormalizeDate(string value)
            {
                if (DateTime.TryParse(value, out var date))
                {
                    return date.ToString("yyyy-MM-dd");
                }
                return value;
            }

            string NormalizeDateTime(string value)
            {
                if (DateTime.TryParse(value, out var dateTime))
                {
                    return dateTime.ToString("yyyy-MM-dd HH:mm");
                }
                return value;
            }

            string rawStatus = GetString("status", "approvalStatus", "approval_status", "result");

            return new OvertimeRequestEntry
            {
                WorkDate = NormalizeDate(GetString("workDate", "work_date", "date")),
                StartTime = GetString("startTime", "start_time", "start"),
                EndTime = GetString("endTime", "end_time", "end"),
                Reason = GetString("reason", "description", "comment"),
                RawStatus = rawStatus,
                Status = TranslateStatus(rawStatus),
                // 승인자 이름: 다양한 필드명 지원 (approvedBy, approved_by, approver, approverName, approver_name)
                Approver = GetString("approver", "approverName", "approvedBy", "approved_by", "approver_name"),
                SubmittedAt = NormalizeDateTime(GetString("createdAt", "created_at", "submittedAt", "submitted_at", "requestDate", "request_date")),
                ApprovedAt = NormalizeDateTime(GetString("approvedAt", "approved_at", "approvalDate", "approval_date", "processedAt", "processed_at")),
                Id = GetString("id", "requestId", "request_id")
            };
        }

        private static string TranslateStatus(string rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus))
            {
                return string.Empty;
            }

            string normalized = rawStatus.Trim().ToUpperInvariant();
            return normalized switch
            {
                "REJECTED" => "반려",
                "APPROVED" => "승인 완료",
                "PENDING" => "접수 중",
                _ => rawStatus
            };
        }

        private static bool IsPending(string rawStatus)
        {
            return string.Equals(rawStatus?.Trim(), "PENDING", StringComparison.OrdinalIgnoreCase);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }
    }

    public class OvertimeRequestEntry
    {
        [Browsable(false)]
        public string Id { get; set; } = string.Empty;

        [DisplayName("근무일")]
        public string WorkDate { get; set; } = string.Empty;

        [DisplayName("시작")]
        public string StartTime { get; set; } = string.Empty;

        [DisplayName("종료")]
        public string EndTime { get; set; } = string.Empty;

        [DisplayName("사유")]
        public string Reason { get; set; } = string.Empty;

        [DisplayName("상태")]
        public string Status { get; set; } = string.Empty;

        [Browsable(false)]
        public string RawStatus { get; set; } = string.Empty;

        [DisplayName("승인자")]
        public string Approver { get; set; } = string.Empty;

        [DisplayName("신청 일시")]
        public string SubmittedAt { get; set; } = string.Empty;

        [DisplayName("승인 일시")]
        public string ApprovedAt { get; set; } = string.Empty;
    }
}