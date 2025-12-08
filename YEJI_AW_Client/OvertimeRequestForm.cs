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
        private readonly Func<DateTime> currentTimeProvider;

        private DateTimePicker dtpWorkDate = new DateTimePicker();
        private DateTimePicker dtpEndTime = new DateTimePicker();
        private TextBox txtReason = new TextBox();
        private Button btnSubmit = new Button();

        private TimeSpan? _pendingStartTimeOverride = null; // 추가 신청 시 기존 종료 시각 이후로 시작

        public OvertimeRequestForm(string serverBaseUrl, HttpClient httpClient, string employeeId, Func<DateTime> currentTimeProvider)
        {
            this.serverBaseUrl = serverBaseUrl;
            this.httpClient = httpClient;
            this.employeeId = employeeId;
            this.currentTimeProvider = currentTimeProvider ?? (() => DateTime.Now);

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "연장 근무 신청";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new System.Drawing.Size(400, 220);
            this.MinimumSize = new System.Drawing.Size(400, 220);
            this.Load += (_, __) => CenterForm();

            var lblWorkDate = new Label { Text = "근무일", AutoSize = true };
            dtpWorkDate.Format = DateTimePickerFormat.Custom;
            dtpWorkDate.CustomFormat = "yyyy-MM-dd";
            dtpWorkDate.Value = currentTimeProvider().Date;

            var lblEndTime = new Label { Text = "연장 종료 시각 (HH:mm)", AutoSize = true };
            dtpEndTime.Format = DateTimePickerFormat.Custom;
            dtpEndTime.CustomFormat = "HH:mm";
            dtpEndTime.ShowUpDown = true;
            dtpEndTime.Width = 120;
            dtpEndTime.Value = currentTimeProvider().Date.AddHours(18);

            var lblReason = new Label { Text = "사유", AutoSize = true };
            txtReason.Multiline = true;
            txtReason.Height = 80;
            txtReason.MinimumSize = new System.Drawing.Size(200, 50);
            txtReason.ScrollBars = ScrollBars.Vertical;
            txtReason.Dock = DockStyle.Fill;

            btnSubmit.Text = "신청";
            btnSubmit.Width = 100;
            btnSubmit.Anchor = AnchorStyles.None;
            btnSubmit.Click += async (s, e) => await SubmitAsync();

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(0),
                AutoSize = true
            };
            buttonPanel.Controls.Add(btnSubmit);          

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(12),
                AutoSize = false
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));

            layout.Controls.Add(lblWorkDate, 0, 0);
            layout.Controls.Add(dtpWorkDate, 1, 0);
            layout.Controls.Add(lblEndTime, 0, 1);
            layout.Controls.Add(dtpEndTime, 1, 1);
            layout.Controls.Add(lblReason, 0, 2);
            layout.Controls.Add(txtReason, 1, 2);
            layout.Controls.Add(buttonPanel, 0, 3);

            layout.SetColumnSpan(txtReason, 1);
            layout.SetColumnSpan(buttonPanel, 2);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            this.Controls.Add(layout);
        }

        private void CenterForm()
        {
            if (this.Owner != null)
            {
                this.CenterToParent();
            }
            else
            {
                this.CenterToScreen();
            }
        }

        private async System.Threading.Tasks.Task SubmitAsync()
        {
            var now = currentTimeProvider();
            var cutoffTime = new TimeSpan(17, 30, 0);

            if (now.TimeOfDay >= cutoffTime)
            {
                MessageBox.Show("연장 근무 신청은 업무시간 내에만 가능합니다.\n업무시간 이전에 승인 요청한 건만 가능합니다.",
                    "신청 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 사유 필수 입력
            var reasonText = txtReason.Text.Trim();
            if (string.IsNullOrWhiteSpace(reasonText))
            {
                MessageBox.Show("사유를 입력해주세요.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtReason.Focus();
                return;
            }

            // 기존 신청과 겹치는지 확인 (같은 날짜의 최대 종료 시각과 비교)
            try
            {
                string date = dtpWorkDate.Value.ToString("yyyy-MM-dd");
                string url = $"{serverBaseUrl}/api/overtime-requests?employeeId={Uri.EscapeDataString(employeeId)}&startDate={date}&endDate={date}";
                string json = await httpClient.GetStringAsync(url);

                TimeSpan ParseTime(string s)
                {
                    return TimeSpan.TryParse(s, out var t) ? t : TimeSpan.Zero;
                }

                TimeSpan newEnd = TimeSpan.Parse(dtpEndTime.Value.ToString("HH:mm"));
                TimeSpan maxExistingEnd = TimeSpan.Zero;
                TimeSpan newStartForAdditional = new TimeSpan(17, 30, 0);

                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    IEnumerable<JsonElement> Enumerate(JsonElement el)
                    {
                        if (el.ValueKind == JsonValueKind.Array) return el.EnumerateArray();
                        if (el.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array) return dataEl.EnumerateArray();
                        if (el.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array) return itemsEl.EnumerateArray();
                        return Array.Empty<JsonElement>();
                    }

                    foreach (var item in Enumerate(root))
                    {
                        var startStr = item.TryGetProperty("startTime", out var st) ? st.ToString() :
                                       item.TryGetProperty("start_time", out var st2) ? st2.ToString() : "17:30";
                        var endStr = item.TryGetProperty("endTime", out var et) ? et.ToString() :
                                     item.TryGetProperty("end_time", out var et2) ? et2.ToString() : string.Empty;
                        var startTs = ParseTime(startStr);
                        var endTs = ParseTime(endStr);
                        if (endTs > maxExistingEnd)
                        {
                            maxExistingEnd = endTs;
                            newStartForAdditional = endTs; // 추가 신청은 기존 종료 이후부터 시작
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
                    var added = newEnd - maxExistingEnd;
                    var confirm = MessageBox.Show($"이미 신청한 시간을 제외한 {added.Hours:D2}:{added.Minutes:D2} 만큼 추가 신청됩니다. 계속 진행하시겠습니까?",
                        "추가 신청", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (confirm != DialogResult.Yes)
                    {
                        return;
                    }

                    // 추가 시간은 신규 등록: 시작 시각을 기존 종료 시각으로 설정
                    _pendingStartTimeOverride = newStartForAdditional;
                }
            }
            catch
            {
                // 겹침 확인 실패 시 계속 진행 (서버에서 검증)
            }

            btnSubmit.Enabled = false;

            var startOverride = _pendingStartTimeOverride.HasValue ? _pendingStartTimeOverride.Value.ToString(@"hh\:mm") : "17:30";

            var payload = new
            {
                employeeId = employeeId,
                workDate = dtpWorkDate.Value.ToString("yyyy-MM-dd"),
                startTime = startOverride,
                endTime = dtpEndTime.Value.ToString("HH:mm"),
                reason = reasonText
            };

            try
            {
                string jsonOut = JsonSerializer.Serialize(payload);
                using var content = new StringContent(jsonOut, Encoding.UTF8, "application/json");
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