using System;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public sealed class ProhibitedUrlAlertForm : Form
    {
        private const int BrowserFocusDelayMs = 200;

        private readonly Panel dialogPanel;
        private readonly IntPtr browserWindowHandle;
        private bool closeCurrentTabRequested;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private const int ASFW_ANY = -1;

        public ProhibitedUrlAlertForm(string companyName, string fullUrl, IntPtr browserWindowHandle)
        {
            this.browserWindowHandle = browserWindowHandle;
            LogDebug($"Alert form created. browserWindowHandle=0x{this.browserWindowHandle.ToInt64():X}");

            Text            = "영업 금지 안내";
            StartPosition   = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            TopMost         = true;
            BackColor       = UiTheme.Background;

            Bounds = ResolveAlertBounds(this.browserWindowHandle);

            dialogPanel = new Panel
            {
                Size        = new Size(520, 360),
                BackColor   = UiTheme.Surface,
                BorderStyle = BorderStyle.FixedSingle
            };

            // ── 헤더 ────────────────────────────────────────────────────
            var headerPanel = UiTheme.MakeHeader("영업 금지 안내");
            headerPanel.Dock = DockStyle.None;
            headerPanel.Size = new Size(dialogPanel.Width, UiTheme.HeaderH);

            // ── 본문 ────────────────────────────────────────────────────
            var bodyPanel = new Panel
            {
                Location  = new Point(0, UiTheme.HeaderH),
                Size      = new Size(dialogPanel.Width, dialogPanel.Height - UiTheme.HeaderH),
                Padding   = new Padding(24, 18, 24, 20),
                BackColor = UiTheme.Surface
            };

            var descriptionLabel = new Label
            {
                AutoSize = false,
                Dock     = DockStyle.Top,
                Height   = 60,
                Font     = UiTheme.Body,
                ForeColor = UiTheme.TextSecondary,
                Text     = "해당 URL은 광고주의 요청으로 영업 및 컨택 금지 업체로 지정되었습니다.\n해당 광고주에게는 전화, 이메일, SMS 등 모든 컨택을 삼가해 주세요."
            };

            string displayCompanyName = string.IsNullOrWhiteSpace(companyName) ? "-" : companyName;

            var companyLabel = new Label
            {
                AutoSize  = true,
                Dock      = DockStyle.Top,
                Padding   = new Padding(0, 8, 0, 2),
                Font      = UiTheme.H3,
                ForeColor = UiTheme.TextPrimary,
                Text      = $"업체명 : {displayCompanyName}"
            };

            var blockedLabel = new Label
            {
                AutoSize  = true,
                Dock      = DockStyle.Top,
                Padding   = new Padding(0, 8, 0, 6),
                Font      = UiTheme.H3,
                ForeColor = UiTheme.TextPrimary,
                Text      = "영업금지 URL :"
            };

            var fullUrlLabel = new Label
            {
                AutoSize     = true,
                Dock         = DockStyle.Top,
                MaximumSize  = new Size(472, 0),
                Font         = UiTheme.Small,
                ForeColor    = UiTheme.TextPrimary,
                Text         = fullUrl
            };

            // ── 버튼 ────────────────────────────────────────────────────
            var backButton = new Button
            {
                Text     = "뒤로가기",
                AutoSize = true,
                Anchor   = AnchorStyles.Right
            };
            UiTheme.StyleOutline(backButton);
            backButton.Click += (_, _) =>
            {
                LogDebug($"Back button clicked. browserWindowHandle=0x{this.browserWindowHandle.ToInt64():X}");
                BrowserUrlMonitor.TrySendBrowserBack(this.browserWindowHandle);
                Close();
            };

            var closeButton = new Button
            {
                Text         = "닫기",
                AutoSize     = true,
                Anchor       = AnchorStyles.Right,
                DialogResult = DialogResult.OK
            };
            UiTheme.StylePrimary(closeButton);
            closeButton.Click += (_, _) =>
            {
                LogDebug($"Close button clicked. browserWindowHandle=0x{this.browserWindowHandle.ToInt64():X}");
                closeCurrentTabRequested = true;
                AllowSetForegroundWindow(ASFW_ANY);
                LogDebug("AllowSetForegroundWindow(ASFW_ANY) called.");
                Close();
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock          = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding       = new Padding(0, 10, 0, 0),
                Height        = 48
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
            Load   += (_, _) => CenterDialogPanel();
            Resize += (_, _) => CenterDialogPanel();
            FormClosed += async (_, _) =>
            {
                if (!closeCurrentTabRequested)
                {
                    LogDebug("Form closed without closeCurrentTabRequested flag. Skipping tab close.");
                    return;
                }

                IntPtr handle = this.browserWindowHandle;
                LogDebug($"Form closed. Will attempt tab close after {BrowserFocusDelayMs}ms. targetHandle=0x{handle.ToInt64():X}");
                await System.Threading.Tasks.Task.Delay(BrowserFocusDelayMs);

                try
                {
                    bool closed = BrowserUrlMonitor.TryCloseCurrentBrowserTab(handle);
                    LogDebug($"TryCloseCurrentBrowserTab result={closed}.");
                    if (!closed)
                        ClientLogger.LogAgent("Failed to close current browser tab after prohibited URL alert closed.", "WRN");
                }
                catch (Exception ex)
                {
                    LogDebug($"Exception while closing tab: {ex.Message}");
                    ClientLogger.LogAgent($"Failed to close browser tab: {ex.Message}", "WRN");
                }
            };
        }

        private static void LogDebug(string message)
        {
#if DEBUG
            ClientLogger.LogAgent($"[URL-ALERT][DEBUG] {message}", "DBG");
#endif
        }

        private static Rectangle ResolveAlertBounds(IntPtr browserHandle)
        {
            if (browserHandle != IntPtr.Zero)
            {
                try { return Screen.FromHandle(browserHandle).Bounds; }
                catch { }
            }
            return Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
        }

        private void CenterDialogPanel()
        {
            dialogPanel.Left = Math.Max(0, (ClientSize.Width  - dialogPanel.Width)  / 2);
            dialogPanel.Top  = Math.Max(0, (ClientSize.Height - dialogPanel.Height) / 2);
        }
    }
}
