using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public class ProhibitedEmailAlertForm : Form
    {
        public ProhibitedEmailAlertForm(List<BanEmailRow> matchedRows)
        {
            Text = "영업 금지 안내";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ShowInTaskbar = false;
            Size = new Size(560, 420);
            BackColor = Color.White;

            var titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 50,
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(192, 57, 43),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(20, 8, 20, 0),
                Text = "영업금지 이메일이 포함되어 있습니다."
            };

            var descriptionLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 62,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(51, 51, 51),
                Padding = new Padding(20, 0, 20, 6),
                Text = "받는사람/참조/숨은참조에 영업금지 이메일이 포함되어 있습니다.\n해당 광고주에게는 전화, 이메일, SMS 등 모든 컨택을 삼가해 주세요."
            };

            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                HorizontalScrollbar = true
            };

            foreach (var row in matchedRows.OrderBy(r => r.Email, StringComparer.OrdinalIgnoreCase))
            {
                string company = string.IsNullOrWhiteSpace(row.CompanyName) ? "-" : row.CompanyName;
                listBox.Items.Add($"{row.Email}  (업체명: {company})");
            }

            var closeButton = new Button
            {
                Text = "확인",
                Width = 100,
                Height = 34,
                DialogResult = DialogResult.OK
            };

            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                Padding = new Padding(0, 10, 20, 10)
            };

            closeButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            closeButton.Location = new Point(buttonPanel.Width - closeButton.Width - 8, 10);
            buttonPanel.Controls.Add(closeButton);
            buttonPanel.Resize += (_, _) =>
            {
                closeButton.Location = new Point(buttonPanel.ClientSize.Width - closeButton.Width, 10);
            };

            Controls.Add(listBox);
            Controls.Add(buttonPanel);
            Controls.Add(descriptionLabel);
            Controls.Add(titleLabel);

            Shown += (_, _) =>
            {
                BringToFront();
                Activate();
            };

            AcceptButton = closeButton;
        }
    }
}