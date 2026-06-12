using System;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client;

/// <summary>
/// 현재 클라이언트의 버전, 연결 상태, heartbeat 시각 등을 보여주는 정보 팝업창입니다.
/// 트레이 메뉴 → "클라이언트 정보"에서 열립니다.
/// </summary>
internal sealed class ClientStatusForm : Form
{
    internal ClientStatusForm(
        string version,
        string employeeId,
        string employeeName,
        string computerName,
        bool isServerReachable,
        DateTime lastHeartbeat,
        TimeSpan workEndTime)
    {
        Text            = "클라이언트 정보";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(400, 340);
        ShowInTaskbar   = false;
        BackColor       = UiTheme.Background;

        // ── 본문 ──────────────────────────────────────────────────────────
        var body = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding   = new Padding(UiTheme.Pad, UiTheme.Pad, UiTheme.Pad, 0)
        };

        // 두 컬럼 테이블 레이아웃
        var table = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 8,
            BackColor   = UiTheme.Surface,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100));

        // 연결 상태 색상·텍스트
        string reachableText  = isServerReachable ? "● 온라인" : "● 오프라인";
        Color  reachableColor = isServerReachable ? UiTheme.Success : UiTheme.Danger;

        // 마지막 heartbeat 표시
        string heartbeatText = lastHeartbeat == default
            ? "아직 응답 없음"
            : lastHeartbeat.ToString("yyyy-MM-dd HH:mm:ss");

        // 업무 종료 시각 표시
        string workEndText = workEndTime == default
            ? "-"
            : $"{(int)workEndTime.TotalHours:D2}:{workEndTime.Minutes:D2}";

        // 구분선으로 그룹 분리
        var rows = new (string label, string value, Color? valueColor)[]
        {
            ("버전",             version,         null),
            ("사번",             employeeId,      null),
            ("사원명",           employeeName,    null),
            ("PC 이름",          computerName,    null),
            (string.Empty,       string.Empty,    null),  // 구분선
            ("서버 연결",        reachableText,   reachableColor),
            ("마지막 응답",      heartbeatText,   null),
            ("업무 종료 시각",   workEndText,     null),
        };

        for (int i = 0; i < rows.Length; i++)
        {
            var (lbl, val, valColor) = rows[i];

            // 빈 행은 구분선으로
            if (string.IsNullOrEmpty(lbl))
            {
                var sep = new Panel
                {
                    BackColor = UiTheme.Border,
                    Height    = 1,
                    Dock      = DockStyle.Fill,
                    Margin    = new Padding(0, 6, 0, 6)
                };
                table.SetColumnSpan(sep, 2);
                table.Controls.Add(sep, 0, i);
                continue;
            }

            var nameLabel = new Label
            {
                Text      = lbl,
                Font      = UiTheme.Small,
                ForeColor = UiTheme.TextSecondary,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(0, 0, 8, 0)
            };

            var valueLabel = new Label
            {
                Text      = val,
                Font      = UiTheme.Body,
                ForeColor = valColor ?? UiTheme.TextPrimary,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            table.Controls.Add(nameLabel,  0, i);
            table.Controls.Add(valueLabel, 1, i);
        }

        // 버튼 바
        var btnPanel = UiTheme.MakeButtonBar();
        var closeBtn = new Button { Text = "닫기", Width = UiTheme.BtnW, DialogResult = DialogResult.OK };
        UiTheme.StylePrimary(closeBtn);
        btnPanel.Controls.Add(closeBtn);

        body.Controls.Add(btnPanel);
        body.Controls.Add(table);

        Controls.Add(body);
        Controls.Add(UiTheme.MakeHeader("클라이언트 정보"));

        AcceptButton = closeBtn;
        CancelButton = closeBtn;
    }
}
