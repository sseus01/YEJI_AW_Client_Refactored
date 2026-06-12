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
        private RoundButton btnSubmit = new RoundButton();

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
            Text            = "PC 종료 예외 등록";
            ClientSize      = new Size(440, 340);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = false;
            MinimizeBox     = false;
            BackColor       = UiTheme.Background;

            // ── 본문 ────────────────────────────────────────────────
            var body = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Padding   = new Padding(UiTheme.Pad, UiTheme.Pad, UiTheme.Pad, 0)
            };

            int labelX = 0, fieldX = 106, rowH = 32, y = 4;
            int fieldW = 290;

            void Row(Label lbl, Control ctrl)
            {
                lbl.AutoSize  = true;
                lbl.Font      = UiTheme.Small;
                lbl.ForeColor = UiTheme.TextSecondary;
                lbl.Location  = new Point(labelX, y + 5);
                ctrl.Location = new Point(fieldX, y);
                ctrl.Size     = new Size(fieldW, 24);
                y += rowH;
            }

            lblEmployeeId.Text = "사번(필수)";
            Row(lblEmployeeId, txtEmployeeId);

            lblWorkDate.Text = "근무일(선택)";
            Row(lblWorkDate, txtWorkDate);
            txtWorkDate.PlaceholderText = "비우면 오늘, YYYY-MM-DD";

            lblFromTime.Text = "시작 시각";
            Row(lblFromTime, txtFromTime);
            txtFromTime.PlaceholderText = "HH:mm";

            lblToTime.Text = "종료 시각";
            Row(lblToTime, txtToTime);
            txtToTime.PlaceholderText = "HH:mm";

            lblReason.Text  = "사유";
            lblReason.AutoSize  = true;
            lblReason.Font      = UiTheme.Small;
            lblReason.ForeColor = UiTheme.TextSecondary;
            lblReason.Location  = new Point(labelX, y + 5);
            txtReason.Location  = new Point(fieldX, y);
            txtReason.Size      = new Size(fieldW, 56);
            txtReason.Multiline = true;
            y += 64;

            lblCreatedBy.Text = "등록자(필수)";
            Row(lblCreatedBy, txtCreatedBy);

            lblHint.Text      = "같은 날짜/사번이면 서버에서 업서트 처리됩니다.";
            lblHint.AutoSize  = true;
            lblHint.Font      = UiTheme.Small;
            lblHint.ForeColor = UiTheme.TextSecondary;
            lblHint.Location  = new Point(0, y + 4);

            body.Controls.AddRange(new Control[]
            {
                lblEmployeeId, txtEmployeeId,
                lblWorkDate,   txtWorkDate,
                lblFromTime,   txtFromTime,
                lblToTime,     txtToTime,
                lblReason,     txtReason,
                lblCreatedBy,  txtCreatedBy,
                lblHint
            });

            var btnPanel = UiTheme.MakeButtonBar();
            btnSubmit.Text  = "등록";
            btnSubmit.Width = UiTheme.BtnW;
            UiTheme.StylePrimary(btnSubmit);
            btnSubmit.Click += async (s, e) => await SubmitAsync();
            btnPanel.Controls.Add(btnSubmit);

            body.Controls.Add(btnPanel);
            Controls.Add(body);
            Controls.Add(UiTheme.MakeFormHeader("PC 종료 예외 등록", null, "+", UiTheme.Primary));
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