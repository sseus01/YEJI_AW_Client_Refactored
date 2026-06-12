using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public class ProhibitedEmailAlertForm : Form
    {
        public ProhibitedEmailAlertForm(List<BanEmailRow> matchedRows, IntPtr browserWindowHandle)
        {
            Text            = "영업 금지 안내";
            StartPosition   = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            TopMost         = true;
            ShowInTaskbar   = false;
            ClientSize      = new Size(560, 400);
            BackColor       = UiTheme.Background;

            // ── 본문 ──────────────────────────────────────────────────
            var body = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Padding   = new Padding(UiTheme.Pad, UiTheme.Pad, UiTheme.Pad, 0)
            };

            var titleLabel = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 36,
                Font      = UiTheme.H3,
                ForeColor = UiTheme.Danger,
                Text      = "영업금지 이메일이 포함되어 있습니다."
            };

            var descLabel = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 48,
                Font      = UiTheme.Body,
                ForeColor = UiTheme.TextSecondary,
                Text      = "받는사람/참조/숨은참조에 영업금지 이메일이 포함되어 있습니다.\n해당 광고주에게는 전화, 이메일, SMS 등 모든 컨택을 삼가해 주세요."
            };

            // 이메일 목록
            var listBox = new ListBox
            {
                Dock               = DockStyle.Fill,
                Font               = UiTheme.Body,
                BorderStyle        = BorderStyle.FixedSingle,
                HorizontalScrollbar = true
            };

            foreach (var row in matchedRows.OrderBy(r => r.Email, StringComparer.OrdinalIgnoreCase))
            {
                string company = string.IsNullOrWhiteSpace(row.CompanyName) ? "-" : row.CompanyName;
                listBox.Items.Add($"{row.Email}  (업체명: {company})");
            }

            // 버튼 패널
            var btnPanel = UiTheme.MakeButtonBar();
            var closeButton = new RoundButton { Text = "확인", Width = UiTheme.BtnW, DialogResult = DialogResult.OK };
            UiTheme.StylePrimary(closeButton);
            btnPanel.Controls.Add(closeButton);

            // 추가 순서: btnPanel(Bottom), 나머지(Top - 역순)
            body.Controls.Add(btnPanel);
            body.Controls.Add(listBox);
            body.Controls.Add(descLabel);
            body.Controls.Add(titleLabel);

            // body(Fill) 먼저, header(Top) 나중에
            Controls.Add(body);
            Controls.Add(UiTheme.MakeFormHeader("영업 금지 안내", null, "!", UiTheme.Danger));

            AcceptButton = closeButton;

            Shown += (_, _) =>
            {
                CenterOnTargetScreen(browserWindowHandle);
                BringToFront();
                Activate();
            };
        }

        private void CenterOnTargetScreen(IntPtr browserWindowHandle)
        {
            Screen targetScreen;
            if (browserWindowHandle != IntPtr.Zero)
            {
                try { targetScreen = Screen.FromHandle(browserWindowHandle); }
                catch { targetScreen = Screen.FromPoint(Cursor.Position); }
            }
            else
            {
                targetScreen = Screen.FromPoint(Cursor.Position);
            }

            Rectangle area = targetScreen.WorkingArea;
            Location = new Point(
                area.Left + Math.Max(0, (area.Width  - Width)  / 2),
                area.Top  + Math.Max(0, (area.Height - Height) / 2));
        }
    }
}
