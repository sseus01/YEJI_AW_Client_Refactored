using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public sealed class ProhibitedUrlAlertForm : Form
    {
        public ProhibitedUrlAlertForm(string domainUrl, string fullUrl)
        {
            Text = "접속 차단 안내";
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
                Text = "접속 차단 안내",
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
                Text = "광고주의 요청에 따라 해당 웹페이지의 접속이 제한되었습니다.\n해당 URL은 영업 및 컨택 금지 업체로 지정되어 접속이 차단됩니다."
            };

            var blockedLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 8, 0, 6),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 34, 34),
                Text = "차단된 URL"
            };

            var urlTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            urlTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            urlTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var domainLabel = new Label
            {
                Text = "도메인",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            var domainValue = CreateUrlTextBox(domainUrl);

            var fullUrlLabel = new Label
            {
                Text = "전체 주소",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            var fullUrlValue = CreateUrlTextBox(fullUrl);

            urlTable.Controls.Add(domainLabel, 0, 0);
            urlTable.Controls.Add(domainValue, 1, 0);
            urlTable.Controls.Add(fullUrlLabel, 0, 1);
            urlTable.Controls.Add(fullUrlValue, 1, 1);

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
            bodyPanel.Controls.Add(urlTable);
            bodyPanel.Controls.Add(blockedLabel);
            bodyPanel.Controls.Add(descriptionLabel);

            Controls.Add(bodyPanel);
            Controls.Add(headerPanel);

            AcceptButton = closeButton;
        }

        private static TextBox CreateUrlTextBox(string text)
        {
            return new TextBox
            {
                Text = text,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Margin = new Padding(0, 2, 0, 6)
            };
        }
    }
}