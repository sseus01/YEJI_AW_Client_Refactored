using System;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client;

/// <summary>
/// 클라이언트 업데이트 다운로드 / 설치 진행 상황을 표시하는 창입니다.
/// DownloadAndInstallUpdateAsync 에서 생성되고, 설치 시작 직전에 자동으로 닫힙니다.
/// </summary>
internal sealed class UpdateProgressForm : Form
{
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Label _percentLabel;

    internal UpdateProgressForm(string currentVersion, string newVersion)
    {
        Text            = "YEJI-ON 업데이트";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = true;
        MaximizeBox     = false;
        ControlBox      = false;
        ClientSize      = new Size(440, 220);
        ShowInTaskbar   = true;
        BackColor       = UiTheme.Background;

        var body = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding   = new Padding(UiTheme.Pad, UiTheme.Pad, UiTheme.Pad, UiTheme.Pad)
        };

        var versionLabel = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 30,
            Text      = $"v{currentVersion}  →  v{newVersion}",
            Font      = UiTheme.H3,
            ForeColor = UiTheme.TextPrimary
        };

        // 진행률 바 + 퍼센트 레이블을 나란히 배치하기 위한 패널
        var progressRow = new Panel
        {
            Dock   = DockStyle.Top,
            Height = 28
        };

        _percentLabel = new Label
        {
            Dock      = DockStyle.Right,
            Width     = 44,
            TextAlign = ContentAlignment.MiddleRight,
            Font      = UiTheme.Small,
            ForeColor = UiTheme.TextSecondary,
            Text      = ""
        };

        _progressBar = new ProgressBar
        {
            Dock                  = DockStyle.Fill,
            Height                = 22,
            Minimum               = 0,
            Maximum               = 100,
            Value                 = 0,
            Style                 = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25
        };

        progressRow.Controls.Add(_progressBar);
        progressRow.Controls.Add(_percentLabel);

        _statusLabel = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 30,
            Padding   = new Padding(0, 8, 0, 0),
            Text      = "준비 중...",
            Font      = UiTheme.Body,
            ForeColor = UiTheme.TextSecondary
        };

        // DockStyle.Top: 나중에 추가될수록 위에 배치
        body.Controls.Add(_statusLabel);
        body.Controls.Add(progressRow);
        body.Controls.Add(versionLabel);

        // Fill body 먼저, Top 헤더 나중에
        Controls.Add(body);
        Controls.Add(UiTheme.MakeFormHeader("YEJI-ON 업데이트", null, "↓", UiTheme.Primary));
    }

    /// <summary>
    /// 진행 상태를 갱신합니다. percent = -1 이면 marquee(무한) 모드로 전환합니다.
    /// UI 스레드 외에서도 안전하게 호출할 수 있습니다.
    /// </summary>
    internal void SetProgress(string status, int percent = -1)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { Invoke(() => SetProgress(status, percent)); return; }

        _statusLabel.Text = status;

        if (percent >= 0)
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.Value = Math.Min(percent, 100);
            _percentLabel.Text = $"{percent}%";
        }
        else
        {
            _progressBar.Style        = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 25;
            _percentLabel.Text        = "";
        }
    }
}
