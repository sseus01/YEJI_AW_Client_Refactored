using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class ShutdownExceptionListForm : Form
    {
        private readonly string serverBaseUrl;
        private readonly System.Net.Http.HttpClient httpClient;
        private readonly string employeeId;

        private Label lblStartDate = new Label();
        private TextBox txtStartDate = new TextBox();
        private Label lblEndDate = new Label();
        private TextBox txtEndDate = new TextBox();
        private RoundButton btnSearch = new RoundButton();
        private DataGridView dataGridView1 = new DataGridView();
        private RoundButton btnRegister = new RoundButton();
        private Label lblStatus = new Label();
        private Label lblEmployeeLabel = new Label();
        private TextBox txtEmployeeId = new TextBox();
        private Label lblDateHint = new Label();

        public ShutdownExceptionListForm(string serverBaseUrl, System.Net.Http.HttpClient httpClient, string employeeId)
        {
            this.serverBaseUrl = serverBaseUrl;
            this.httpClient = httpClient;
            this.employeeId = employeeId;

            BuildLayout();
            txtEmployeeId.Text = employeeId;

            Load += ShutdownExceptionListForm_Load;
        }

        private void BuildLayout()
        {
            Text          = "PC 종료 예외 조회";
            ClientSize    = new Size(800, 500);
            BackColor     = UiTheme.Background;

            // ── 필터 바 ─────────────────────────────────────────────
            var filterPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 60,
                BackColor = UiTheme.Surface
            };

            lblStartDate.Text      = "시작일(선택)";
            lblStartDate.AutoSize  = true;
            lblStartDate.Font      = UiTheme.Small;
            lblStartDate.ForeColor = UiTheme.TextSecondary;
            lblStartDate.Location  = new Point(12, 10);

            txtStartDate.Location        = new Point(88, 7);
            txtStartDate.Size            = new Size(100, 23);
            txtStartDate.PlaceholderText = "YYYY-MM-DD";

            lblEndDate.Text      = "종료일(선택)";
            lblEndDate.AutoSize  = true;
            lblEndDate.Font      = UiTheme.Small;
            lblEndDate.ForeColor = UiTheme.TextSecondary;
            lblEndDate.Location  = new Point(196, 10);

            txtEndDate.Location        = new Point(276, 7);
            txtEndDate.Size            = new Size(100, 23);
            txtEndDate.PlaceholderText = "YYYY-MM-DD";

            btnSearch.Text     = "조회";
            btnSearch.Location = new Point(384, 3);
            btnSearch.Size     = new Size(68, UiTheme.BtnH);
            UiTheme.StylePrimary(btnSearch);
            btnSearch.Click += btnSearch_Click;

            btnRegister.Text     = "등록/수정";
            btnRegister.Anchor   = AnchorStyles.Top | AnchorStyles.Right;
            btnRegister.Location = new Point(720, 3);
            btnRegister.Size     = new Size(68, UiTheme.BtnH);
            UiTheme.StyleSecondary(btnRegister);
            btnRegister.Click += btnRegister_Click;

            lblEmployeeLabel.Text      = "사번";
            lblEmployeeLabel.AutoSize  = true;
            lblEmployeeLabel.Font      = UiTheme.Small;
            lblEmployeeLabel.ForeColor = UiTheme.TextSecondary;
            lblEmployeeLabel.Location  = new Point(12, 38);

            txtEmployeeId.Location = new Point(52, 35);
            txtEmployeeId.ReadOnly = true;
            txtEmployeeId.Size     = new Size(100, 23);

            lblDateHint.Text      = "날짜 미입력 시 최근 30일 기준";
            lblDateHint.AutoSize  = true;
            lblDateHint.Font      = UiTheme.Small;
            lblDateHint.ForeColor = UiTheme.TextSecondary;
            lblDateHint.Location  = new Point(160, 38);

            lblStatus.AutoSize  = false;
            lblStatus.TextAlign = ContentAlignment.MiddleRight;
            lblStatus.Anchor    = AnchorStyles.Top | AnchorStyles.Right;
            lblStatus.Font      = UiTheme.Small;
            lblStatus.ForeColor = UiTheme.TextSecondary;
            lblStatus.Location  = new Point(560, 38);
            lblStatus.Size      = new Size(228, 18);

            filterPanel.Controls.AddRange(new Control[]
            {
                lblStartDate, txtStartDate, lblEndDate, txtEndDate, btnSearch, btnRegister,
                lblEmployeeLabel, txtEmployeeId, lblDateHint, lblStatus
            });

            // ── DataGridView ────────────────────────────────────────
            dataGridView1.Dock                = DockStyle.Fill;
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            UiTheme.StyleDataGridView(dataGridView1);

            Controls.Add(dataGridView1);
            Controls.Add(filterPanel);
            Controls.Add(UiTheme.MakeFormHeader("PC 종료 예외 조회", null, "≡", UiTheme.Primary));
        }

        private async void ShutdownExceptionListForm_Load(object? sender, EventArgs e)
        {
            await RefreshListAsync();
        }

        private async void btnSearch_Click(object? sender, EventArgs e)
        {
            await RefreshListAsync();
        }

        private async void btnRegister_Click(object? sender, EventArgs e)
        {
            using var editForm = new ShutdownExceptionRegisterForm(serverBaseUrl, httpClient, employeeId);
            if (editForm.ShowDialog() == DialogResult.OK)
            {
                await RefreshListAsync();
            }
        }

        private async System.Threading.Tasks.Task RefreshListAsync()
        {
            btnSearch.Enabled = false;
            btnRegister.Enabled = false;
            lblStatus.Text = "조회 중...";

            try
            {
                var urlBuilder = new StringBuilder($"{serverBaseUrl}/api/shutdown-exceptions?employeeId={Uri.EscapeDataString(employeeId)}");
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

                dataGridView1.AutoGenerateColumns = true;
                dataGridView1.DataSource = items;
                lblStatus.Text = $"총 {items.Count}건";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PC 종료 예외를 불러오는데 실패했습니다.\n{ex.Message}");
                lblStatus.Text = "오류 발생";
            }
            finally
            {
                btnSearch.Enabled = true;
                btnRegister.Enabled = true;
            }
        }

        private void ShowErrorMessage(System.Net.HttpStatusCode statusCode, string serverMessage)
        {
            string message = statusCode switch
            {
                System.Net.HttpStatusCode.BadRequest => "요청 값이 올바르지 않습니다. 필수 입력을 확인해주세요.",
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

        private static List<ShutdownExceptionEntry> ParseResponse(string json, JsonSerializerOptions jsonOptions)
        {
            string preview = json.Length > 300 ? json.Substring(0, 300) + "..." : json;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<ShutdownExceptionEntry>>(json, jsonOptions)
                        ?? new List<ShutdownExceptionEntry>();
                }

                if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<ShutdownExceptionEntry>>(dataElement.GetRawText(), jsonOptions)
                        ?? new List<ShutdownExceptionEntry>();
                }

                if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<ShutdownExceptionEntry>>(itemsElement.GetRawText(), jsonOptions)
                        ?? new List<ShutdownExceptionEntry>();
                }

                if (root.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<ShutdownExceptionEntry>>(contentElement.GetRawText(), jsonOptions)
                        ?? new List<ShutdownExceptionEntry>();
                }
            }
            catch (JsonException)
            {
                // parsing will be handled below
            }

            throw new InvalidOperationException($"서버 응답을 처리할 수 없습니다. 관리자에게 문의하세요. 응답 미리보기: {preview}");
        }
    }
}