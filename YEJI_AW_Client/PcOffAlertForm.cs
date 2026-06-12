using System;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client;

internal sealed class PcOffAlertForm : Form
{
    private readonly Label       _countdownLabel;
    private readonly Label       _tempDisableBadge;
    private readonly Label       _urgentBadge;
    private readonly RoundButton _tempDisableBtn;
    private readonly Panel       _progressFill;
    private double               _totalSeconds = -1;

    /// <summary>사용자가 '일시해제 신청' 버튼을 클릭했을 때 발생합니다.</summary>
    internal event EventHandler? TempDisableClicked;

    internal PcOffAlertForm(string headline, bool canUseTempDisable, int remainingTempDisableCount, DateTime shutdownTime)
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
        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = UiTheme.Surface };
        btnPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border });

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
        btnPanel.Layout += (_, _) => btnFlow.Location = new Point(
            btnPanel.ClientSize.Width - btnFlow.Width - UiTheme.Pad,
            (btnPanel.ClientSize.Height - btnFlow.Height) / 2 + 2);

        // ── 본문 ─────────────────────────────────────────────────────
        var body = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Padding   = new Padding(UiTheme.Pad, 12, UiTheme.Pad, 8)
        };

        // ── 상태 배지 행 (카드 아래) ──────────────────────────────────
        var badgeRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Top,
            Height        = 36,
            BackColor     = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            Padding       = new Padding(0, 8, 0, 0)
        };
        _urgentBadge = UiTheme.MakeStatusBadge("강제종료 예정", UiTheme.BadgeStyle.Danger);
        _urgentBadge.Visible = false;
        var scheduleBadge = UiTheme.MakeStatusBadge($"{shutdownTime:HH:mm} 설정됨", UiTheme.BadgeStyle.Gray);
        badgeRow.Controls.Add(_urgentBadge);
        badgeRow.Controls.Add(scheduleBadge);

        // ── 카운트다운 카드 ───────────────────────────────────────────
        var cdBox = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 100,
            BackColor = UiTheme.Surface
        };
        cdBox.Paint += (s, e) =>
        {
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, cdBox.Width - 1, cdBox.Height - 1);
        };

        // 진행 바 (카드 하단)
        var progressTrack = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 6,
            BackColor = UiTheme.AccentLight
        };
        _progressFill = new Panel
        {
            Location  = Point.Empty,
            Height    = 6,
            Width     = 0,
            BackColor = UiTheme.Accent
        };
        progressTrack.Controls.Add(_progressFill);
        progressTrack.Resize += (_, _) =>
        {
            if (_totalSeconds > 0) _progressFill.Height = progressTrack.ClientSize.Height;
        };

        // 카드 내부: 좌(카운트다운) / 우(배지 정보) 2열
        var cdTable = new TableLayoutPanel
        {
            Dock            = DockStyle.Fill,
            ColumnCount     = 2,
            RowCount        = 1,
            BackColor       = Color.Transparent,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        cdTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        cdTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));

        // 왼쪽: 카운트다운 숫자 + "남은 시간"
        var leftFlow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            BackColor     = Color.Transparent,
            Padding       = new Padding(20, 10, 0, 0),
            WrapContents  = false
        };
        _countdownLabel = new Label
        {
            Text      = "--:--",
            Font      = UiTheme.CountdownLg,
            ForeColor = UiTheme.Accent,
            BackColor = Color.Transparent,
            AutoSize  = true
        };
        var remainingLbl = new Label
        {
            Text      = "남은 시간",
            Font      = UiTheme.Small,
            ForeColor = UiTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Margin    = new Padding(3, 0, 0, 0)
        };
        leftFlow.Controls.Add(_countdownLabel);
        leftFlow.Controls.Add(remainingLbl);

        // 오른쪽: 일시해제 배지 + 퇴근 기준 레이블
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _tempDisableBadge = UiTheme.MakeStatusBadge(
            showTempDisable ? $"일시해제 {remainingTempDisableCount}회 남음" : "　",
            UiTheme.BadgeStyle.Warning);
        _tempDisableBadge.Visible = showTempDisable;

        var scheduleInfoLbl = new Label
        {
            Text      = $"오늘 {shutdownTime:HH:mm} 퇴근 기준",
            Font      = UiTheme.Small,
            ForeColor = UiTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true
        };

        rightPanel.Controls.Add(_tempDisableBadge);
        rightPanel.Controls.Add(scheduleInfoLbl);
        rightPanel.Layout += (_, _) =>
        {
            int rightEdge = rightPanel.ClientSize.Width - 16;
            _tempDisableBadge.Location = new Point(rightEdge - _tempDisableBadge.Width, 14);
            scheduleInfoLbl.Location   = new Point(rightEdge - scheduleInfoLbl.PreferredWidth, _tempDisableBadge.Bottom + 4);
        };

        cdTable.Controls.Add(leftFlow,   0, 0);
        cdTable.Controls.Add(rightPanel, 1, 0);

        cdBox.Controls.Add(progressTrack);
        cdBox.Controls.Add(cdTable);

        // Dock=Top는 나중에 추가될수록 위에 배치됨
        body.Controls.Add(badgeRow);
        body.Controls.Add(cdBox);

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

    /// <summary>매 초 호출하여 남은 시간 표시를 갱신합니다.</summary>
    internal void UpdateCountdown(TimeSpan remaining, int remainingTempDisableCount)
    {
        if (IsDisposed || !IsHandleCreated) return;

        if (_totalSeconds < 0)
            _totalSeconds = Math.Max(remaining.TotalSeconds, 1);

        _countdownLabel.Text = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
            : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";

        bool urgent = remaining < TimeSpan.FromMinutes(1);
        _countdownLabel.ForeColor = urgent ? UiTheme.Danger : UiTheme.Accent;

        double pct    = Math.Max(0, Math.Min(1, remaining.TotalSeconds / _totalSeconds));
        int    trackW = _progressFill.Parent?.ClientSize.Width ?? 0;
        if (trackW > 0)
        {
            _progressFill.Width     = (int)(trackW * pct);
            _progressFill.Height    = _progressFill.Parent!.ClientSize.Height;
            _progressFill.BackColor = urgent ? UiTheme.Danger : UiTheme.Accent;
        }

        if (urgent) _urgentBadge.Visible = true;

        if (remainingTempDisableCount > 0)
        {
            string badgeText = $"일시해제 {remainingTempDisableCount}회 남음";
            _tempDisableBadge.Text    = badgeText;
            _tempDisableBadge.Width   = TextRenderer.MeasureText(badgeText, UiTheme.BadgeFont).Width + 16;
            _tempDisableBadge.Visible = true;
        }
        else
        {
            _tempDisableBadge.Visible = false;
        }
    }

    /// <summary>일시해제 신청이 실패했을 때 호출합니다.</summary>
    internal void OnTempDisableFailed(string message)
    {
        if (IsDisposed) return;
        _tempDisableBtn.Visible   = false;
        _tempDisableBadge.Visible = false;
        _urgentBadge.Visible      = true;
    }
}
