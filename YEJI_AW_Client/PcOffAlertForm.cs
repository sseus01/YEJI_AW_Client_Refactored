using System;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client;

/// <summary>
/// PC 종료 알림창 전용 Form 클래스.
/// Form1에서 분리되어 UI와 레이아웃을 독립적으로 관리합니다.
/// </summary>
internal sealed class PcOffAlertForm : Form
{
    private readonly Label _countdownLabel;
    private readonly Label _statusLabel;
    private readonly Button _tempDisableBtn;

    /// <summary>사용자가 '일시해제 신청' 버튼을 클릭했을 때 발생합니다.</summary>
    internal event EventHandler? TempDisableClicked;

    internal PcOffAlertForm(string headline, bool canUseTempDisable, int remainingTempDisableCount)
    {
        // ── Form 기본 설정 ────────────────────────────────────────────────
        Text            = "PC 종료 알림";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(720, 260);
        TopMost         = true;
        ShowInTaskbar   = false;
        BackColor       = UiTheme.Background;

        // ── 본문 영역 ────────────────────────────────────────────────────
        var body = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding   = new Padding(UiTheme.Pad, UiTheme.Pad, UiTheme.Pad, 0)
        };

        // 헤드라인 (최대 2줄)
        var headlineLabel = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 50,
            Text      = headline,
            Font      = UiTheme.H3,
            ForeColor = UiTheme.TextPrimary,
            AutoSize  = false
        };

        // 카운트다운 (색상 강조 박스)
        _countdownLabel = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 52,
            Text      = "",
            Font      = UiTheme.CountdownLg,
            ForeColor = UiTheme.Warning,
            BackColor = UiTheme.WarningLight,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(UiTheme.Pad, 0, 0, 0)
        };

        // 상태 텍스트 (일시해제 가능 횟수 또는 경고 메시지)
        string initStatus = canUseTempDisable && remainingTempDisableCount > 0
            ? $"일시 해제 신청 가능 횟수: {remainingTempDisableCount}회"
            : "";
        _statusLabel = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 26,
            Text      = initStatus,
            Font      = UiTheme.Small,
            ForeColor = UiTheme.TextSecondary,
            Padding   = new Padding(2, 6, 0, 0)
        };

        // 버튼 패널 (하단 우측 정렬)
        var btnPanel = UiTheme.MakeButtonBar();

        var closeBtn = new Button { Text = "PC 종료", Width = UiTheme.BtnW };
        UiTheme.StyleDanger(closeBtn);
        closeBtn.Click += (_, _) => Close();

        bool showTempDisable = canUseTempDisable && remainingTempDisableCount > 0;
        _tempDisableBtn = new Button
        {
            Text    = "일시해제 신청",
            Width   = UiTheme.BtnW,
            Visible = showTempDisable
        };
        UiTheme.StyleSecondary(_tempDisableBtn);
        _tempDisableBtn.Click += (_, _) => TempDisableClicked?.Invoke(this, EventArgs.Empty);

        _tempDisableBtn.TabIndex = 0;
        closeBtn.TabIndex        = 1;
        btnPanel.Controls.Add(closeBtn);
        btnPanel.Controls.Add(_tempDisableBtn);

        // DockStyle.Top 컨트롤은 나중에 추가될수록 위에 배치됨
        // 추가 순서: btnPanel(Bottom), _statusLabel, _countdownLabel, headlineLabel
        body.Controls.Add(btnPanel);
        body.Controls.Add(_statusLabel);
        body.Controls.Add(_countdownLabel);
        body.Controls.Add(headlineLabel);

        // DockStyle.Fill body는 먼저, DockStyle.Top 헤더는 나중에 추가
        Controls.Add(body);
        Controls.Add(UiTheme.MakeHeader("PC 종료 알림"));

        AcceptButton = showTempDisable ? _tempDisableBtn : closeBtn;
        CancelButton = closeBtn;
        if (showTempDisable)
            ActiveControl = _tempDisableBtn;
    }

    /// <summary>
    /// 매 초 Form1의 카운트다운 타이머에서 호출하여 남은 시간 표시를 갱신합니다.
    /// </summary>
    internal void UpdateCountdown(TimeSpan remaining, int remainingTempDisableCount)
    {
        if (IsDisposed || !IsHandleCreated) return;

        // 남은 시간 텍스트
        _countdownLabel.Text = remaining.TotalMinutes >= 1
            ? $"남은 시간: {remaining.Minutes}분 {remaining.Seconds:D2}초"
            : $"남은 시간: {(int)remaining.TotalSeconds}초";

        // 1분 미만이면 주황 → 빨간색으로 전환
        bool urgent = remaining < TimeSpan.FromMinutes(1);
        _countdownLabel.BackColor = urgent ? UiTheme.DangerLight  : UiTheme.WarningLight;
        _countdownLabel.ForeColor = urgent ? UiTheme.Danger : UiTheme.Warning;

        // 일시해제 불가 상태에서 1분 미만 → 초 단위 강제 종료 경고
        if (remainingTempDisableCount <= 0 && urgent)
        {
            _statusLabel.Text      = $"{(int)remaining.TotalSeconds}초 후 PC가 강제 종료됩니다.";
            _statusLabel.ForeColor = UiTheme.Danger;
        }
    }

    /// <summary>
    /// 일시해제 신청이 실패했을 때 Form1에서 호출합니다.
    /// 버튼을 숨기고 오류 메시지를 표시합니다.
    /// </summary>
    internal void OnTempDisableFailed(string message)
    {
        if (IsDisposed) return;
        _tempDisableBtn.Visible = false;
        _statusLabel.Text       = message;
        _statusLabel.ForeColor  = UiTheme.Danger;
    }
}
