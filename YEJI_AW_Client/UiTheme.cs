using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client;

/// <summary>
/// 앱 전체 색상·폰트·버튼 스타일을 한 곳에서 관리합니다.
/// 모든 Form에서 이 클래스를 참조하고, 직접 RGB/폰트를 하드코딩하지 않습니다.
/// </summary>
internal static class UiTheme
{
    // ── 브랜드 색상 ──────────────────────────────────────────────────────────
    internal static readonly Color Primary      = Color.FromArgb(33,  85, 168);  // 헤더·주요 버튼
    internal static readonly Color PrimaryDark  = Color.FromArgb(7,   67, 140);  // 강조 hover
    internal static readonly Color PrimaryLight = Color.FromArgb(213, 220, 235); // 선택 배경

    // ── 중립 색상 ────────────────────────────────────────────────────────────
    internal static readonly Color Background   = Color.FromArgb(247, 248, 250);
    internal static readonly Color Surface      = Color.White;
    internal static readonly Color Border       = Color.FromArgb(218, 220, 224);
    internal static readonly Color Selection    = Color.FromArgb(213, 220, 228);

    // ── 텍스트 색상 ──────────────────────────────────────────────────────────
    internal static readonly Color TextPrimary   = Color.FromArgb(28,  28,  28);
    internal static readonly Color TextSecondary = Color.FromArgb(90,  90,  90);
    internal static readonly Color TextOnPrimary = Color.White;

    // ── 상태 색상 ────────────────────────────────────────────────────────────
    internal static readonly Color Warning      = Color.FromArgb(245, 124,   0);
    internal static readonly Color WarningLight = Color.FromArgb(255, 243, 224);
    internal static readonly Color Danger       = Color.FromArgb(211,  47,  47);
    internal static readonly Color DangerLight  = Color.FromArgb(255, 235, 238);
    internal static readonly Color Success      = Color.FromArgb(46,  125,  50);

    // ── 폰트 ─────────────────────────────────────────────────────────────────
    private const string FF = "Segoe UI";

    internal static Font H1           { get; } = new Font(FF, 14F, FontStyle.Bold);
    internal static Font H2           { get; } = new Font(FF, 12F, FontStyle.Bold);
    internal static Font H3           { get; } = new Font(FF, 10F, FontStyle.Bold);
    internal static Font Body         { get; } = new Font(FF, 10F, FontStyle.Regular);
    internal static Font Small        { get; } = new Font(FF,  9F, FontStyle.Regular);
    internal static Font Mono         { get; } = new Font("Consolas", 9F, FontStyle.Regular);
    internal static Font CountdownLg  { get; } = new Font(FF, 18F, FontStyle.Bold);
    internal static Font CountdownMd  { get; } = new Font(FF, 14F, FontStyle.Bold);

    // ── 레이아웃 상수 ────────────────────────────────────────────────────────
    internal const int HeaderH = 52;
    internal const int BtnH    = 34;
    internal const int BtnW    = 130;
    internal const int Pad     = 16;

    // ── 버튼 스타일 헬퍼 ─────────────────────────────────────────────────────

    internal static void StylePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Primary;
        b.ForeColor = TextOnPrimary;
        b.Font      = Body;
        b.Height    = BtnH;
        b.Cursor    = Cursors.Hand;
    }

    internal static void StyleSecondary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize  = 1;
        b.FlatAppearance.BorderColor = Primary;
        b.BackColor = Color.FromArgb(235, 241, 255);
        b.ForeColor = Primary;
        b.Font      = Body;
        b.Height    = BtnH;
        b.Cursor    = Cursors.Hand;
    }

    internal static void StyleDanger(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Danger;
        b.ForeColor = TextOnPrimary;
        b.Font      = Body;
        b.Height    = BtnH;
        b.Cursor    = Cursors.Hand;
    }

    internal static void StyleOutline(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize  = 1;
        b.FlatAppearance.BorderColor = Border;
        b.BackColor = Surface;
        b.ForeColor = TextPrimary;
        b.Font      = Body;
        b.Height    = BtnH;
        b.Cursor    = Cursors.Hand;
    }

    // ── 공통 컨트롤 팩토리 ───────────────────────────────────────────────────

    /// <summary>파란 헤더 패널을 생성합니다.</summary>
    internal static Panel MakeHeader(string title)
    {
        var p = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = HeaderH,
            BackColor = Primary,
            Padding   = new Padding(Pad, 0, Pad, 0)
        };
        p.Controls.Add(new Label
        {
            Dock      = DockStyle.Fill,
            Text      = title,
            Font      = H2,
            ForeColor = TextOnPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        });
        return p;
    }

    /// <summary>하단 버튼 줄에 사용하는 FlowLayoutPanel을 생성합니다.</summary>
    internal static FlowLayoutPanel MakeButtonBar()
        => new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height        = BtnH + Pad + 4,
            Padding       = new Padding(Pad, 4, Pad, 0),
            BackColor     = Surface
        };
}
