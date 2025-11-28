using System;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class ShutdownExceptionRegisterForm : Form
    {
        private readonly string serverBaseUrl;
        private readonly HttpClient httpClient;

        private Label lblEmployeeId = new Label();
        private Label lblWorkDate = new Label();
        private Label lblFromTime = new Label();
        private Label lblToTime = new Label();
        private Label lblReason = new Label();
        private Label lblCreatedBy = new Label();
        private Label lblHint = new Label();

        private TextBox txtEmployeeId = new TextBox();
        private TextBox txtWorkDate = new TextBox();
        private TextBox txtFromTime = new TextBox();
        private TextBox txtToTime = new TextBox();
        private TextBox txtReason = new TextBox();
        private TextBox txtCreatedBy = new TextBox();
        private Button btnSubmit = new Button();

        public ShutdownExceptionRegisterForm(string serverBaseUrl, HttpClient httpClient, string employeeId)
        {
            this.serverBaseUrl = serverBaseUrl;
            this.httpClient = httpClient;

            BuildLayout();

            txtEmployeeId.Text = employeeId;
            txtCreatedBy.Text = Environment.UserName;
        }

        private void BuildLayout()
        {
            Text = "PC 종료 예외 등록";
            ClientSize = new Size(416, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            lblEmployeeId.Text = "사번(필수)";
            lblEmployeeId.AutoSize = true;
            lblEmployeeId.Location = new Point(12, 15);

            lblWorkDate.Text = "근무일(선택)";
            lblWorkDate.AutoSize = true;
            lblWorkDate.Location = new Point(12, 44);

            lblFromTime.Text = "시작 시각";
            lblFromTime.AutoSize = true;
            lblFromTime.Location = new Point(12, 73);

            lblToTime.Text = "종료 시각";
            lblToTime.AutoSize = true;
            lblToTime.Location = new Point(12, 102);

            lblReason.Text = "사유";
            lblReason.AutoSize = true;
            lblReason.Location = new Point(12, 131);

            lblCreatedBy.Text = "등록자(필수)";
            lblCreatedBy.AutoSize = true;
            lblCreatedBy.Location = new Point(12, 196);

            lblHint.Text = "같은 날짜/사번이면 서버에서 업서트 처리";
            lblHint.AutoSize = true;
            lblHint.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblHint.Location = new Point(12, 228);

            txtEmployeeId.Location = new Point(101, 12);
            txtEmployeeId.Size = new Size(171, 23);

            txtWorkDate.Location = new Point(101, 41);
            txtWorkDate.Size = new Size(171, 23);
            txtWorkDate.PlaceholderText = "비우면 오늘, 형식: YYYY-MM-DD";

            txtFromTime.Location = new Point(101, 70);
            txtFromTime.Size = new Size(171, 23);
            txtFromTime.PlaceholderText = "HH:mm 또는 HH:mm:ss";

            txtToTime.Location = new Point(101, 99);
            txtToTime.Size = new Size(171, 23);
            txtToTime.PlaceholderText = "HH:mm 또는 HH:mm:ss";

            txtReason.Location = new Point(101, 128);
            txtReason.Size = new Size(303, 60);
            txtReason.Multiline = true;

            txtCreatedBy.Location = new Point(101, 193);
            txtCreatedBy.Size = new Size(171, 23);

            btnSubmit.Text = "등록";
            btnSubmit.Location = new Point(329, 223);
            btnSubmit.Size = new Size(75, 25);
            btnSubmit.Click += async (s, e) => await SubmitAsync();

            Controls.AddRange(new Control[]
            {
                lblEmployeeId, lblWorkDate, lblFromTime, lblToTime, lblReason, lblCreatedBy, lblHint,
                txtEmployeeId, txtWorkDate, txtFromTime, txtToTime, txtReason, txtCreatedBy, btnSubmit
            });
        }

        private async System.Threading.Tasks.Task SubmitAsync()
        {
            btnSubmit.Enabled = false;

            if (string.IsNullOrWhiteSpace(txtEmployeeId.Text))
            {
                MessageBox.Show("사번은 필수입니다.");
                btnSubmit.Enabled = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(txtFromTime.Text) || string.IsNullOrWhiteSpace(txtToTime.Text))
            {
                MessageBox.Show("시작/종료 시각은 필수입니다. HH:mm 형식으로 입력해주세요.");
                btnSubmit.Enabled = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(txtCreatedBy.Text))
            {
                MessageBox.Show("등록자는 필수입니다.");
                btnSubmit.Enabled = true;
                return;
            }

            var payload = new
            {
                employeeId = txtEmployeeId.Text.Trim(),
                workDate = string.IsNullOrWhiteSpace(txtWorkDate.Text) ? null : txtWorkDate.Text.Trim(),
                fromTime = txtFromTime.Text.Trim(),
                toTime = txtToTime.Text.Trim(),
                reason = txtReason.Text.Trim(),
                createdBy = txtCreatedBy.Text.Trim()
            };

            try
            {
                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await httpClient.PostAsync($"{serverBaseUrl}/api/shutdown-exceptions", content);

                if (!response.IsSuccessStatusCode)
                {
                    string serverMessage = await response.Content.ReadAsStringAsync();
                    ShowErrorMessage(response.StatusCode, serverMessage);
                    return;
                }

                MessageBox.Show("등록/수정이 완료되었습니다.");
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

            MessageBox.Show(message, "등록 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}