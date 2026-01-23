using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public sealed class ProhibitedUrlAlertForm : Form
    {
        public ProhibitedUrlAlertForm(string fullUrl)
        {
            Text = "영업 금지 안내";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(243, 246, 251);
            ClientSize = new Size(520, 360);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.FromArgb(33, 85, 168)
            };

            var headerLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "영업 금지 안내",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold)
            };
            headerPanel.Controls.Add(headerLabel);

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 18, 24, 20),
                BackColor = Color.White
            };

            var descriptionLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 64,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(51, 51, 51),
                Text = "해당 URL은 광고주의 요청으로 영업 및 컨택 금지 업체로 지정되었습니다.\n해당 광고주에게는 전화, 이메일, SMS등 모든 컨텍을 삼가해주길 바랍니다."
            };

            var blockedLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 8, 0, 6),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 34, 34),
                Text = "영업금지 URL"
            };
                      
            var fullUrlLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                MaximumSize = new Size(472, 0),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(34, 34, 34),
                Text = fullUrl
            };

            var noteLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(0, 12, 0, 0),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(86, 86, 86),
                Text = "접속 해제가 필요할 경우 기업문화팀으로 문의해 주세요."
            };

            var closeButton = new Button
            {
                Text = "닫기",
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                DialogResult = DialogResult.OK
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 10, 0, 0),
                Height = 44
            };
            buttonPanel.Controls.Add(closeButton);

            bodyPanel.Controls.Add(buttonPanel);
            bodyPanel.Controls.Add(noteLabel);
            bodyPanel.Controls.Add(fullUrlLabel);
            bodyPanel.Controls.Add(blockedLabel);
            bodyPanel.Controls.Add(descriptionLabel);

            Controls.Add(bodyPanel);
            Controls.Add(headerPanel);

            AcceptButton = closeButton;
        }
    }
}