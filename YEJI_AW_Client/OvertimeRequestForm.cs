using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using System.Drawing;

namespace YEJI_AW_Client
{
    public partial class OvertimeRequestForm : Form
    {
        private readonly string serverBaseUrl;
        private readonly HttpClient httpClient;
        private readonly string employeeId;
        private readonly Func<DateTime> currentTimeProvider;

        private readonly DateTimePicker dtpWorkDate;
        private readonly DateTimePicker dtpEndTime;
        private readonly TextBox txtReason;
        private readonly RoundButton btnSubmit;

        private TimeSpan? _pendingStartTimeOverride = null;

        public OvertimeRequestForm(string serverBaseUrl, HttpClient httpClient, string employeeId, Func<DateTime> currentTimeProvider)
        {
            this.serverBaseUrl      = serverBaseUrl;
            this.httpClient         = httpClient;
            this.employeeId         = employeeId;
            this.currentTimeProvider = currentTimeProvider ?? (() => DateTime.Now);

            Text            = "연장 근무 신청";
            StartPosition   = FormStartPosition.CenterScreen;
            ClientSize      = new Size(520, 400);
            MinimumSize     = new Size(520, 400);
            BackColor       = UiTheme.Background;
            MaximizeBox     = false;

            // ── 본문 ────────────────────────────────────────────────
            var body = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Padding   = new Padding(UiTheme.Pad)
            };

            var layout = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 2,
                RowCount    = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.BtnH + UiTheme.Pad + 8));

            // 근무일
            var lblWorkDate = new Label { Text = "근무일", Font = UiTheme.Small, ForeColor = UiTheme.TextSecondary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            dtpWorkDate = new DateTimePicker
            {
                Format       = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd",
                Value        = currentTimeProvider().Date,
                Dock         = DockStyle.Fill
            };

            // 연장 종료 시각
            var lblEndTime = new Label { Text = "연장 종료 시각", Font = UiTheme.Small, ForeColor = UiTheme.TextSecondary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            dtpEndTime = new DateTimePicker
            {
                Format       = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown   = true,
                Dock         = DockStyle.Fill,
                Value        = currentTimeProvider().Date.AddHours(18)
            };

            // 사유
            var lblReason = new Label { Text = "사유 (필수)", Font = UiTheme.Small, ForeColor = UiTheme.TextSecondary, Dock = DockStyle.Top, Height = 20 };
            txtReason = new TextBox
            {
                Multiline       = true,
                Dock            = DockStyle.Fill,
                ScrollBars      = ScrollBars.Vertical,
                PlaceholderText = "사유를 입력하세요(필수)",
                Font            = UiTheme.Body
            };
            var reasonCell = new Panel { Dock = DockStyle.Fill };
            reasonCell.Controls.Add(txtReason);
            reasonCell.Controls.Add(lblReason);

            // 버튼 바
            var btnPanel = UiTheme.MakeButtonBar();

            btnSubmit = new RoundButton { Text = "신청", Width = UiTheme.BtnW };
            UiTheme.StylePrimary(btnSubmit);
            btnSubmit.Click += async (_, _) => await SubmitAsync();

            var btnCancel = new RoundButton { Text = "닫기", Width = UiTheme.BtnW };
            UiTheme.StyleOutline(btnCancel);
            btnCancel.Click += (_, _) => Close();

            btnPanel.Controls.Add(btnSubmit);
            btnPanel.Controls.Add(btnCancel);

            layout.Controls.Add(lblWorkDate,  0, 0);
            layout.Controls.Add(dtpWorkDate,  1, 0);
            layout.Controls.Add(lblEndTime,   0, 1);
            layout.Controls.Add(dtpEndTime,   1, 1);
            layout.Controls.Add(reasonCell,   0, 2);
            layout.SetColumnSpan(reasonCell, 2);
            layout.Controls.Add(btnPanel,     0, 3);
            layout.SetColumnSpan(btnPanel, 2);

            body.Controls.Add(layout);

            Controls.Add(body);
            Controls.Add(UiTheme.MakeFormHeader("연장 근무 신청", null, "◷", UiTheme.Primary));

            Load += (_, _) =>
            {
                if (Owner != null) CenterToParent();
                else CenterToScreen();
            };
        }

        private async System.Threading.Tasks.Task SubmitAsync()
        {
            var now        = currentTimeProvider();
            var cutoffTime = new TimeSpan(17, 30, 0);

            if (now.TimeOfDay >= cutoffTime)
            {
                MessageBox.Show("연장 근무 신청은 업무시간 내에만 가능합니다.\n업무시간 이전에 승인 요청한 건만 가능합니다.",
                    "신청 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var reasonText = txtReason.Text.Trim();
            if (string.IsNullOrWhiteSpace(reasonText))
            {
                MessageBox.Show("사유를 입력해주세요.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtReason.Focus();
                return;
            }

            try
            {
                string date = dtpWorkDate.Value.ToString("yyyy-MM-dd");
                string url  = $"{serverBaseUrl}/api/overtime-requests?employeeId={Uri.EscapeDataString(employeeId)}&startDate={date}&endDate={date}";
                string json = await httpClient.GetStringAsync(url);

                TimeSpan ParseTime(string s) => TimeSpan.TryParse(s, out var t) ? t : TimeSpan.Zero;

                TimeSpan newEnd = TimeSpan.Parse(dtpEndTime.Value.ToString("HH:mm"));
                TimeSpan maxExistingEnd = TimeSpan.Zero;
                TimeSpan newStartForAdditional = new TimeSpan(17, 30, 0);

                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    IEnumerable<System.Text.Json.JsonElement> Enumerate(System.Text.Json.JsonElement el)
                    {
                        if (el.ValueKind == System.Text.Json.JsonValueKind.Array) return el.EnumerateArray();
                        if (el.TryGetProperty("data",  out var d) && d.ValueKind  == System.Text.Json.JsonValueKind.Array) return d.EnumerateArray();
                        if (el.TryGetProperty("items", out var i) && i.ValueKind == System.Text.Json.JsonValueKind.Array) return i.EnumerateArray();
                        return Array.Empty<System.Text.Json.JsonElement>();
                    }

                    foreach (var item in Enumerate(root))
                    {
                        var statusStr =
                            item.TryGetProperty("status",          out var sp1) ? sp1.ToString() :
                            item.TryGetProperty("approvalStatus",  out var sp2) ? sp2.ToString() :
                            item.TryGetProperty("approval_status", out var sp3) ? sp3.ToString() :
                            item.TryGetProperty("result",          out var sp4) ? sp4.ToString() : string.Empty;

                        if (string.Equals(statusStr?.Trim(), "REJECTED", StringComparison.OrdinalIgnoreCase)) continue;

                        var startStr = item.TryGetProperty("startTime", out var st)  ? st.ToString()  :
                                       item.TryGetProperty("start_time", out var st2) ? st2.ToString() : "17:30";
                        var endStr   = item.TryGetProperty("endTime",   out var et)  ? et.ToString()  :
                                       item.TryGetProperty("end_time",   out var et2) ? et2.ToString() : string.Empty;
                        var endTs    = ParseTime(endStr);
                        if (endTs > maxExistingEnd)
                        {
                            maxExistingEnd        = endTs;
                            newStartForAdditional = endTs;
                        }
                    }
                }

                if (maxExistingEnd >= newEnd)
                {
                    MessageBox.Show("해당 날짜의 연장 시간은 이미 신청되었습니다.", "중복 신청", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                else if (maxExistingEnd > TimeSpan.Zero && newEnd > maxExistingEnd)
                {
                    var added   = newEnd - maxExistingEnd;
                    var confirm = MessageBox.Show(
                        $"이미 신청한 시간을 제외한 {added.Hours:D2}:{added.Minutes:D2} 만큼 추가 신청됩니다. 계속 진행하시겠습니까?",
                        "추가 신청", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (confirm != DialogResult.Yes) return;
                    _pendingStartTimeOverride = newStartForAdditional;
                }
            }
            catch { }

            btnSubmit.Enabled = false;

            var startOverride = _pendingStartTimeOverride.HasValue
                ? _pendingStartTimeOverride.Value.ToString(@"hh\:mm")
                : "17:30";

            var payload = new
            {
                employeeId = employeeId,
                workDate   = dtpWorkDate.Value.ToString("yyyy-MM-dd"),
                startTime  = startOverride,
                endTime    = dtpEndTime.Value.ToString("HH:mm"),
                reason     = reasonText
            };

            try
            {
                string jsonOut = JsonSerializer.Serialize(payload);
                using var content  = new StringContent(jsonOut, Encoding.UTF8, "application/json");
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
                System.Net.HttpStatusCode.NotFound   => "대상 데이터를 찾을 수 없습니다.",
                _                                    => "서버 처리 중 문제가 발생했습니다."
            };
            if (!string.IsNullOrWhiteSpace(serverMessage))
                message += $"\n서버 메시지: {serverMessage}";
            MessageBox.Show(message, "신청 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
