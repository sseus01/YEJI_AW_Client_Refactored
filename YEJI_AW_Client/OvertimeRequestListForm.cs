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
        private Button btnSearch = new Button();
        private Button btnDelete = new Button();
        private DataGridView dgvRequests = new DataGridView();
        private Label lblStatus = new Label();
        private Font? buttonFont;
        private Font? gridHeaderFont;

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
            Text = "연장근무신청 확인";
            ClientSize = new Size(820, 480);            
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(240, 240, 240);

            // 애플리케이션 아이콘 설정
            try
            {
                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrayIcon_Y_orange.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    Icon = new Icon(iconPath);
                }
            }
            catch { /* 아이콘 로드 실패 시 무시 */ }

            lblStartDate.Text = "시작일";
            lblStartDate.AutoSize = true;
            lblStartDate.Location = new Point(12, 15);

            dtpStartDate.Format = DateTimePickerFormat.Custom;
            dtpStartDate.CustomFormat = "yyyy-MM-dd";
            dtpStartDate.Location = new Point(73, 12);
            dtpStartDate.Size = new Size(120, 23);
            dtpStartDate.Value = DateTime.Today; // 기본 오늘

            lblEndDate.Text = "종료일";
            lblEndDate.AutoSize = true;
            lblEndDate.Location = new Point(199, 15);

            dtpEndDate.Format = DateTimePickerFormat.Custom;
            dtpEndDate.CustomFormat = "yyyy-MM-dd";
            dtpEndDate.Location = new Point(256, 12);
            dtpEndDate.Size = new Size(120, 23);
            dtpEndDate.Value = DateTime.Today; // 기본 오늘

            btnSearch.Text = "조회";
            btnSearch.Location = new Point(382, 11);
            btnSearch.Size = new Size(75, 30);
            btnSearch.BackColor = Color.FromArgb(70, 130, 180);
            btnSearch.ForeColor = Color.White;
            btnSearch.FlatStyle = FlatStyle.Flat;
            buttonFont = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            btnSearch.Font = buttonFont;
            btnSearch.Cursor = Cursors.Hand;
            btnSearch.FlatAppearance.BorderSize = 0;
            btnSearch.FlatAppearance.MouseOverBackColor = Color.FromArgb(100, 150, 200);
            btnSearch.Click += BtnSearch_Click;

            btnDelete.Text = "취소"; // 삭제 → 취소
            btnDelete.Location = new Point(463, 11);
            btnDelete.Size = new Size(75, 30);
            btnDelete.BackColor = Color.FromArgb(70, 130, 180);
            btnDelete.ForeColor = Color.White;
            btnDelete.FlatStyle = FlatStyle.Flat;
            btnDelete.Font = buttonFont;
            btnDelete.Cursor = Cursors.Hand;
            btnDelete.FlatAppearance.BorderSize = 0;
            btnDelete.FlatAppearance.MouseOverBackColor = Color.FromArgb(100, 150, 200);
            btnDelete.Enabled = false;
            btnDelete.Click += BtnDelete_Click;

            dgvRequests.Location = new Point(12, 50);
            dgvRequests.Size = new Size(796, 418);
            dgvRequests.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgvRequests.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvRequests.ReadOnly = true;
            dgvRequests.AllowUserToAddRows = false;
            dgvRequests.AllowUserToDeleteRows = false;
            dgvRequests.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvRequests.MultiSelect = false;
            dgvRequests.DataBindingComplete += DgvRequests_DataBindingComplete;
            dgvRequests.SelectionChanged += DgvRequests_SelectionChanged;

            // DataGridView 스타일 개선
            ImproveDataGridViewStyle(dgvRequests);

            lblStatus.AutoSize = false;
            lblStatus.TextAlign = ContentAlignment.MiddleRight;
            lblStatus.Location = new Point(585, 17);
            lblStatus.Size = new Size(223, 15);
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            Controls.AddRange(new Control[]
            {
                lblStartDate, dtpStartDate, lblEndDate, dtpEndDate, btnSearch, btnDelete,
                dgvRequests, lblStatus
            });
        }

        private void ImproveDataGridViewStyle(DataGridView dgv)
        {
            // 헤더 스타일
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(70, 130, 180); // Steel Blue
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            gridHeaderFont = new Font(dgv.Font.FontFamily, 9F, FontStyle.Bold);
            dgv.ColumnHeadersDefaultCellStyle.Font = gridHeaderFont;
            dgv.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgv.ColumnHeadersHeight = 32;

            // 셀 스타일
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(173, 216, 230); // Light Blue
            dgv.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgv.DefaultCellStyle.BackColor = Color.White;
            dgv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            dgv.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            dgv.RowTemplate.Height = 28;

            // 그리드 라인
            dgv.GridColor = Color.FromArgb(220, 220, 220);
            dgv.BorderStyle = BorderStyle.Fixed3D;
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
            if (disposing)
            {
                buttonFont?.Dispose();
                gridHeaderFont?.Dispose();
            }
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