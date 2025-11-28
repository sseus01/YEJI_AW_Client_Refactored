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
        private TextBox txtStartDate = new TextBox();
        private Label lblEndDate = new Label();
        private TextBox txtEndDate = new TextBox();
        private Button btnSearch = new Button();
        private Button btnDelete = new Button();
        private DataGridView dgvRequests = new DataGridView();
        private Label lblStatus = new Label();
        private Label lblEmployeeLabel = new Label();
        private TextBox txtEmployeeId = new TextBox();
        private Label lblDateHint = new Label();

        private BindingList<OvertimeRequestEntry> currentItems = new BindingList<OvertimeRequestEntry>();

        public OvertimeRequestListForm(string serverBaseUrl, System.Net.Http.HttpClient httpClient, string employeeId)
        {
            this.serverBaseUrl = serverBaseUrl;
            this.httpClient = httpClient;
            this.employeeId = employeeId;

            BuildLayout();
            txtEmployeeId.Text = employeeId;

            Load += OvertimeRequestListForm_Load;
        }

        private void BuildLayout()
        {
            Text = "연장 근무 신청 결과";
            ClientSize = new Size(820, 480);

            lblStartDate.Text = "시작일(선택)";
            lblStartDate.AutoSize = true;
            lblStartDate.Location = new Point(12, 15);

            txtStartDate.Location = new Point(93, 12);
            txtStartDate.Size = new Size(110, 23);
            txtStartDate.PlaceholderText = "YYYY-MM-DD";

            lblEndDate.Text = "종료일(선택)";
            lblEndDate.AutoSize = true;
            lblEndDate.Location = new Point(209, 15);

            txtEndDate.Location = new Point(290, 12);
            txtEndDate.Size = new Size(110, 23);
            txtEndDate.PlaceholderText = "YYYY-MM-DD";

            btnSearch.Text = "조회";
            btnSearch.Location = new Point(406, 11);
            btnSearch.Size = new Size(75, 25);
            btnSearch.Click += BtnSearch_Click;

            btnDelete.Text = "삭제";
            btnDelete.Location = new Point(487, 11);
            btnDelete.Size = new Size(75, 25);
            btnDelete.Enabled = false;
            btnDelete.Click += BtnDelete_Click;

            dgvRequests.Location = new Point(12, 72);
            dgvRequests.Size = new Size(796, 396);
            dgvRequests.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgvRequests.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvRequests.ReadOnly = true;
            dgvRequests.AllowUserToAddRows = false;
            dgvRequests.AllowUserToDeleteRows = false;
            dgvRequests.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvRequests.MultiSelect = false;
            dgvRequests.DataBindingComplete += DgvRequests_DataBindingComplete;
            dgvRequests.SelectionChanged += DgvRequests_SelectionChanged;

            lblStatus.AutoSize = false;
            lblStatus.TextAlign = ContentAlignment.MiddleRight;
            lblStatus.Location = new Point(585, 17);
            lblStatus.Size = new Size(223, 15);
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            lblEmployeeLabel.Text = "사번(필수)";
            lblEmployeeLabel.AutoSize = true;
            lblEmployeeLabel.Location = new Point(12, 44);

            txtEmployeeId.Location = new Point(93, 41);
            txtEmployeeId.ReadOnly = true;
            txtEmployeeId.Size = new Size(110, 23);

            lblDateHint.Text = "입력하지 않으면 최근 30일을 기본으로 조회합니다.";
            lblDateHint.AutoSize = true;
            lblDateHint.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblDateHint.Location = new Point(209, 45);

            Controls.AddRange(new Control[]
            {
                lblStartDate, txtStartDate, lblEndDate, txtEndDate, btnSearch, btnDelete,
                dgvRequests, lblStatus, lblEmployeeLabel, txtEmployeeId, lblDateHint
            });
        }

        private async void OvertimeRequestListForm_Load(object? sender, EventArgs e)
        {
            await RefreshListAsync();
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

                if (!string.IsNullOrWhiteSpace(txtStartDate.Text))
                {
                    urlBuilder.Append($"&startDate={Uri.EscapeDataString(txtStartDate.Text.Trim())}");
                }

                if (!string.IsNullOrWhiteSpace(txtEndDate.Text))
                {
                    urlBuilder.Append($"&endDate={Uri.EscapeDataString(txtEndDate.Text.Trim())}");
                }

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
                MessageBox.Show("삭제할 신청을 선택해주세요.");
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                MessageBox.Show("선택한 신청 건의 식별자를 찾을 수 없습니다. 관리자에게 문의하세요.");
                return;
            }

            if (!IsPending(entry.RawStatus))
            {
                MessageBox.Show("접수 중 상태의 신청만 삭제할 수 있습니다.");
                return;
            }

            var confirm = MessageBox.Show("선택한 신청을 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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

                MessageBox.Show("신청 건이 삭제되었습니다.");
                await RefreshListAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"삭제 중 오류가 발생했습니다.\\n{ex.Message}");
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
                Approver = GetString("approver", "approverName", "approvedBy", "approver_name"),
                SubmittedAt = NormalizeDateTime(GetString("createdAt", "created_at", "submittedAt", "submitted_at", "requestDate", "request_date")),
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
    }
}