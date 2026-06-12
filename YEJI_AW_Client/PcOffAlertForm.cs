using System;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client;

internal sealed class PcOffAlertForm : Form
{
    private readonly Label       _countdownLabel;
    private readonly Label       _statusLabel;
    private readonly RoundButton _tempDisableBtn;
    private readonly Panel       _progressFill;
    private double               _totalSeconds = -1;

    /// <summary>사용자가 '일시해제 신청' 버튼을 클릭했을 때 발생합니다.</summary>
    internal event EventHandler? TempDisableClicked;

    internal PcOffAlertForm(string headline, bool canUseTempDisable, int remainingTempDisableCount)
    {
        Text            = "PC 종료 알림";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(560, 340);
        TopMost         = true;
        ShowInTaskbar   = false;
        BackColor       = UiTheme.Background;

        // ── 버튼 패널 (하단) ─────────────────────────────────────────
        var btnPanel = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 60,
            BackColor = UiTheme.Surface
        };
        btnPanel.Controls.Add(new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 1,
            BackColor = UiTheme.Border
        });

        var closeBtn = new RoundButton { Text = "지금 종료", Width = 110, Height = UiTheme.BtnH };
        UiTheme.StyleDanger(closeBtn);
        closeBtn.Click += (_, _) => Close();

        bool showTempDisable = canUseTempDisable && remainingTempDisableCount > 0;
        _tempDisableBtn = new RoundButton
        {
            Text    = "일시해제 신청",
            Width   = 120,
            Height  = UiTheme.BtnH,
            Visible = showTempDisable
        };
        UiTheme.StylePrimary(_tempDisableBtn);
        _tempDisableBtn.Click += (_, _) => TempDisableClicked?.Invoke(this, EventArgs.Empty);

        // 버튼들을 우측에 배치
        var btnFlow = new FlowLayoutPanel
        {
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents  = false,
            Anchor        = AnchorStyles.Right | AnchorStyles.Top,
            Padding       = new Padding(0)
        };
        btnFlow.Controls.Add(closeBtn);
        btnFlow.Controls.Add(_tempDisableBtn);
        btnPanel.Controls.Add(btnFlow);

        // Layout 이벤트로 버튼 패널 우측에 고정
        btnPanel.Layout += (_, _) =>
        {
            btnFlow.Location = new Point(
                btnPanel.ClientSize.Width - btnFlow.Width - UiTheme.Pad,
                (btnPanel.ClientSize.Height - btnFlow.Height) / 2 + 2);
        };

        // ── 본문 ─────────────────────────────────────────────────────
        var body = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Padding   = new Padding(UiTheme.Pad, 12, UiTheme.Pad, 8)
        };

        // 카운트다운 박스
        var cdBox = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 110,
            BackColor = UiTheme.PrimaryLight,
            Padding   = new Padding(20, 14, 20, 0)
        };

        _countdownLabel = new Label
        {
            Dock      = DockStyle.Fill,
            Text      = "--:--",
            Font      = UiTheme.CountdownLg,
            ForeColor = UiTheme.Primary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };

        // 커스텀 진행 바 (진행 트랙 + 채움 패널)
        var progressTrack = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 6,
            BackColor = Color.FromArgb(196, 212, 248)
        };
        _progressFill = new Panel
        {
            Location  = Point.Empty,
            Height    = 6,
            Width     = 0,
            BackColor = UiTheme.Primary
        };
        progressTrack.Controls.Add(_progressFill);
        // 트랙 크기 변경 시 채움 너비도 재계산 (초기 레이아웃 후)
        progressTrack.Resize += (_, _) =>
        {
            if (_totalSeconds > 0)
                _progressFill.Height = progressTrack.ClientSize.Height;
        };

        cdBox.Controls.Add(progressTrack);
        cdBox.Controls.Add(_countdownLabel);

        // 상태 레이블
        _statusLabel = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 30,
            Padding   = new Padding(0, 8, 0, 0),
            Text      = showTempDisable ? $"일시해제 {remainingTempDisableCount}회 남음" : "",
            Font      = UiTheme.Small,
            ForeColor = UiTheme.Primary,
            BackColor = Color.Transparent
        };

        // Top 컨트롤은 나중에 추가될수록 위에 배치
        body.Controls.Add(_statusLabel);
        body.Controls.Add(cdBox);

        // Form: Bottom → Fill → Top 순서
        var header = UiTheme.MakeFormHeader("PC 종료 예정 안내", headline, "⏻", UiTheme.Danger);
        Controls.Add(btnPanel);
        Controls.Add(body);
        Controls.Add(header);

        if (showTempDisable)
        {
            AcceptButton  = _tempDisableBtn;
            ActiveControl = _tempDisableBtn;
        }
        else
        {
            AcceptButton = closeBtn;
        }
        CancelButton = closeBtn;
    }

    /// <summary>매 초 Form1의 카운트다운 타이머에서 호출하여 남은 시간 표시를 갱신합니다.</summary>
    internal void UpdateCountdown(TimeSpan remaining, int remainingTempDisableCount)
    {
        if (IsDisposed || !IsHandleCreated) return;

        if (_totalSeconds < 0)
            _totalSeconds = Math.Max(remaining.TotalSeconds, 1);

        // 카운트다운 텍스트 (MM:SS 또는 HH:MM:SS)
        _countdownLabel.Text = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
            : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";

        // 1분 미만 → 빨간색으로 전환
        bool urgent = remaining < TimeSpan.FromMinutes(1);
        _countdownLabel.ForeColor = urgent ? UiTheme.Danger   : UiTheme.Primary;

        // 진행 바 업데이트
        double pct    = Math.Max(0, Math.Min(1, remaining.TotalSeconds / _totalSeconds));
        int    trackW = _progressFill.Parent?.ClientSize.Width ?? 0;
        if (trackW > 0)
        {
            _progressFill.Width     = (int)(trackW * pct);
            _progressFill.Height    = _progressFill.Parent!.ClientSize.Height;
            _progressFill.BackColor = urgent ? UiTheme.Danger : UiTheme.Primary;
        }

        // 상태 텍스트
        if (urgent && remainingTempDisableCount <= 0)
        {
            _statusLabel.Text      = $"{(int)remaining.TotalSeconds}초 후 PC가 강제 종료됩니다.";
            _statusLabel.ForeColor = UiTheme.Danger;
        }
        else if (remainingTempDisableCount > 0)
        {
            _statusLabel.Text      = $"일시해제 {remainingTempDisableCount}회 남음";
            _statusLabel.ForeColor = urgent ? UiTheme.Warning : UiTheme.Primary;
        }
    }

    /// <summary>일시해제 신청이 실패했을 때 호출합니다.</summary>
    internal void OnTempDisableFailed(string message)
    {
        if (IsDisposed) return;
        _tempDisableBtn.Visible = false;
        _statusLabel.Text       = message;
        _statusLabel.ForeColor  = UiTheme.Danger;
    }
}
