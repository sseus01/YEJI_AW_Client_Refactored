using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class OvertimeRequestForm : Form
    {
        private readonly string serverBaseUrl;
        private readonly HttpClient httpClient;
        private readonly string employeeId;

        private DateTimePicker dtpWorkDate = new DateTimePicker();
        private DateTimePicker dtpStartTime = new DateTimePicker();
        private DateTimePicker dtpEndTime = new DateTimePicker();
        private TextBox txtReason = new TextBox();
        private Button btnSubmit = new Button();

        public OvertimeRequestForm(string serverBaseUrl, HttpClient httpClient, string employeeId)
        {
            this.serverBaseUrl = serverBaseUrl;
            this.httpClient = httpClient;
            this.employeeId = employeeId;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "연장 근무 신청";
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new System.Drawing.Size(420, 320);

            var lblWorkDate = new Label { Text = "근무일", AutoSize = true };
            dtpWorkDate.Format = DateTimePickerFormat.Custom;
            dtpWorkDate.CustomFormat = "yyyy-MM-dd";
            dtpWorkDate.Value = DateTime.Today;

            var lblStartTime = new Label { Text = "시작 시각 (HH:mm)", AutoSize = true };
            dtpStartTime.Format = DateTimePickerFormat.Custom;
            dtpStartTime.CustomFormat = "HH:mm";
            dtpStartTime.ShowUpDown = true;
            dtpStartTime.Width = 120;
            dtpStartTime.Value = DateTime.Today.Add(DateTime.Now.TimeOfDay);

            var lblEndTime = new Label { Text = "종료 시각 (HH:mm)", AutoSize = true };
            dtpEndTime.Format = DateTimePickerFormat.Custom;
            dtpEndTime.CustomFormat = "HH:mm";
            dtpEndTime.ShowUpDown = true;
            dtpEndTime.Width = 120;
            dtpEndTime.Value = DateTime.Today.Add(DateTime.Now.TimeOfDay);

            var lblReason = new Label { Text = "사유", AutoSize = true };
            txtReason.Multiline = true;
            txtReason.Height = 120;
            txtReason.ScrollBars = ScrollBars.Vertical;

            btnSubmit.Text = "신청";
            btnSubmit.Width = 100;
            btnSubmit.Anchor = AnchorStyles.Right;
            btnSubmit.Click += async (s, e) => await SubmitAsync();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(10),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

            layout.Controls.Add(lblWorkDate, 0, 0);
            layout.Controls.Add(dtpWorkDate, 1, 0);
            layout.Controls.Add(lblStartTime, 0, 1);
            layout.Controls.Add(dtpStartTime, 1, 1);
            layout.Controls.Add(lblEndTime, 0, 2);
            layout.Controls.Add(dtpEndTime, 1, 2);
            layout.Controls.Add(lblReason, 0, 3);
            layout.Controls.Add(txtReason, 1, 3);
            layout.Controls.Add(btnSubmit, 1, 4);

            layout.SetColumnSpan(txtReason, 1);
            layout.SetCellPosition(btnSubmit, new TableLayoutPanelCellPosition(1, 4));
            layout.SetColumnSpan(btnSubmit, 1);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            this.Controls.Add(layout);
        }

        private async System.Threading.Tasks.Task SubmitAsync()
        {
            btnSubmit.Enabled = false;
                  
            var payload = new
            {
                employeeId = employeeId,
                workDate = dtpWorkDate.Value.ToString("yyyy-MM-dd"),
                startTime = dtpStartTime.Value.ToString("HH:mm"),
                endTime = dtpEndTime.Value.ToString("HH:mm"),
                reason = txtReason.Text.Trim()
            };

            try
            {
                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await httpClient.PostAsync($"{serverBaseUrl}/api/overtime-requests", content);

                if (!response.IsSuccessStatusCode)
                {
                    string serverMessage = await response.Content.ReadAsStringAsync();
                    ShowErrorMessage(response.StatusCode, serverMessage);
                    return;
                }

                MessageBox.Show("연장 근무 신청이 접수되었습니다.");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"요청 중 오류가 발생했습니다.\n{ex.Message}");
            }
            finally
            {
                btnSubmit.Enabled = true;
            }
        }

        private void ShowErrorMessage(System.Net.HttpStatusCode statusCode, string serverMessage)
        {
            string message = statusCode switch
            {
                System.Net.HttpStatusCode.BadRequest => "필수 입력이 누락되었거나 형식이 잘못되었습니다.",
                System.Net.HttpStatusCode.NotFound => "대상 데이터를 찾을 수 없습니다.",
                _ => "서버 처리 중 문제가 발생했습니다."
            };

            if (!string.IsNullOrWhiteSpace(serverMessage))
            {
                message += $"\n서버 메시지: {serverMessage}";
            }

            MessageBox.Show(message, "신청 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}