using System;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public sealed class ProhibitedUrlAlertForm : Form
    {
        // 폼이 닫힌 뒤 OS가 브라우저 창에 포커스를 돌려줄 때까지 대기하는 시간(밀리초)
        private const int BrowserFocusDelayMs = 200;

        private readonly Panel dialogPanel;
        private readonly IntPtr browserWindowHandle;
        private bool closeCurrentTabRequested;

        public ProhibitedUrlAlertForm(string companyName, string fullUrl, IntPtr browserWindowHandle)
        {
            this.browserWindowHandle = browserWindowHandle;
            Text = "영업 금지 안내";
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(240, 243, 249);

            Bounds = ResolveAlertBounds(this.browserWindowHandle);

            dialogPanel = new Panel
            {
                Size = new Size(520, 360),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

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

            string displayCompanyName = string.IsNullOrWhiteSpace(companyName) ? "-" : companyName;

            var companyLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 8, 0, 2),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 34, 34),
                Text = $"업체명 : {displayCompanyName}"
            };

            var blockedLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 8, 0, 6),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 34, 34),
                Text = "영업금지 URL :"
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
            
            var backButton = new Button
            {
                Text = "뒤로가기",
                AutoSize = true,
                Anchor = AnchorStyles.Right
            };
            backButton.Click += (_, _) =>
            {
                BrowserUrlMonitor.TrySendBrowserBack(this.browserWindowHandle);
                Close();
            };

            var closeButton = new Button
            {
                Text = "닫기1",
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                DialogResult = DialogResult.OK
            };
            closeButton.Click += (_, _) =>
            {
                closeCurrentTabRequested = true;
                Close();
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 10, 0, 0),
                Height = 44
            };
            buttonPanel.Controls.Add(closeButton);
            buttonPanel.Controls.Add(backButton);           

            bodyPanel.Controls.Add(buttonPanel);            
            bodyPanel.Controls.Add(fullUrlLabel);
            bodyPanel.Controls.Add(blockedLabel);
            bodyPanel.Controls.Add(companyLabel);
            bodyPanel.Controls.Add(descriptionLabel);

            dialogPanel.Controls.Add(bodyPanel);
            dialogPanel.Controls.Add(headerPanel);

            Controls.Add(dialogPanel);

            bool hasBrowserHandle = this.browserWindowHandle != IntPtr.Zero;
            backButton.Enabled = hasBrowserHandle;

            AcceptButton = closeButton;
            Load += (_, _) => CenterDialogPanel();
            Resize += (_, _) => CenterDialogPanel();
            FormClosed += (_, _) =>
            {
                if (closeCurrentTabRequested)
                {
                    // 폼이 완전히 닫히고 브라우저 창이 포커스를 되찾은 뒤에
                    // 탭 닫기를 실행해야 SendInput 키 입력이 올바른 창으로 전달된다.
                    IntPtr handle = this.browserWindowHandle;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(BrowserFocusDelayMs);
                        try
                        {
                            BrowserUrlMonitor.TryCloseCurrentBrowserTab(handle);
                        }
                        catch (Exception ex)
                        {
                            ClientLogger.LogAgent($"Failed to close browser tab: {ex.Message}", "WRN");
                        }
                    });
                }
            };
        }

        private static Rectangle ResolveAlertBounds(IntPtr browserHandle)
        {
            if (browserHandle != IntPtr.Zero)
            {
                try
                {
                    return Screen.FromHandle(browserHandle).Bounds;
                }
                catch
                {
                    // 브라우저 핸들이 유효하지 않으면 기본 모니터로 폴백
                }
            }

            return Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
        }

        private void CenterDialogPanel()
        {
            dialogPanel.Left = Math.Max(0, (ClientSize.Width - dialogPanel.Width) / 2);
            dialogPanel.Top = Math.Max(0, (ClientSize.Height - dialogPanel.Height) / 2);
        }
    }
}