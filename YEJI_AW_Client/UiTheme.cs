using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client;

internal static class UiTheme
{
    // ── 브랜드 색상 ──────────────────────────────────────────────────────────
    internal static readonly Color Primary      = Color.FromArgb(43,  95, 224);  // #2B5FE0 인디고
    internal static readonly Color PrimaryDark  = Color.FromArgb(26,  63, 160);  // hover
    internal static readonly Color PrimaryLight = Color.FromArgb(232, 240, 254); // 배경 틴트

    // ── 보조 색상 ────────────────────────────────────────────────────────────
    internal static readonly Color Accent      = Color.FromArgb(108, 63, 214);  // 퍼플
    internal static readonly Color AccentLight = Color.FromArgb(243, 239, 254);

    // ── 중립 색상 ────────────────────────────────────────────────────────────
    internal static readonly Color Background  = Color.FromArgb(248, 249, 252);
    internal static readonly Color Surface     = Color.White;
    internal static readonly Color Border      = Color.FromArgb(208, 216, 232);
    internal static readonly Color Selection   = Color.FromArgb(232, 240, 254);

    // ── 텍스트 색상 ──────────────────────────────────────────────────────────
    internal static readonly Color TextPrimary   = Color.FromArgb(17,  17,  17);
    internal static readonly Color TextSecondary = Color.FromArgb(120, 130, 148);
    internal static readonly Color TextOnPrimary = Color.White;

    // ── 상태 색상 ────────────────────────────────────────────────────────────
    internal static readonly Color Warning      = Color.FromArgb(179, 107,   0);
    internal static readonly Color WarningLight = Color.FromArgb(255, 244, 224);
    internal static readonly Color Danger       = Color.FromArgb(192,  57,  43);
    internal static readonly Color DangerLight  = Color.FromArgb(255, 234, 234);
    internal static readonly Color Success      = Color.FromArgb(26,  122,  66);
    internal static readonly Color SuccessLight = Color.FromArgb(228, 245, 236);

    // ── 폰트 ─────────────────────────────────────────────────────────────────
    private const string FF = "Segoe UI";

    internal static Font H1          { get; } = new Font(FF, 14F, FontStyle.Bold);
    internal static Font H2          { get; } = new Font(FF, 12F, FontStyle.Bold);
    internal static Font H3          { get; } = new Font(FF, 10F, FontStyle.Bold);
    internal static Font Body        { get; } = new Font(FF, 10F, FontStyle.Regular);
    internal static Font Small       { get; } = new Font(FF,  9F, FontStyle.Regular);
    internal static Font Mono        { get; } = new Font("Consolas", 9F, FontStyle.Regular);
    internal static Font CountdownLg { get; } = new Font(FF, 24F, FontStyle.Bold);
    internal static Font CountdownMd { get; } = new Font(FF, 14F, FontStyle.Bold);
    internal static Font BadgeFont   { get; } = new Font(FF,  8F, FontStyle.Bold);

    // ── 레이아웃 상수 ────────────────────────────────────────────────────────
    internal const int HeaderH    = 52;  // 하위 호환용
    internal const int FormTitleH = 72;  // MakeFormHeader 아이콘+제목 영역
    internal const int BtnH       = 36;
    internal const int BtnW       = 120;
    internal const int BtnRadius  =  8;
    internal const int Pad        = 16;

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

    internal static void StylePrimary(RoundButton b) { StylePrimary((Button)b); b.Radius = BtnRadius; }

    internal static void StyleSecondary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize  = 1;
        b.FlatAppearance.BorderColor = Primary;
        b.BackColor = PrimaryLight;
        b.ForeColor = Primary;
        b.Font      = Body;
        b.Height    = BtnH;
        b.Cursor    = Cursors.Hand;
    }

    internal static void StyleSecondary(RoundButton b) { StyleSecondary((Button)b); b.Radius = BtnRadius; }

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

    internal static void StyleDanger(RoundButton b) { StyleDanger((Button)b); b.Radius = BtnRadius; }

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

    internal static void StyleOutline(RoundButton b) { StyleOutline((Button)b); b.Radius = BtnRadius; }

    /// <summary>DataGridView에 앱 공통 스타일을 적용합니다.</summary>
    internal static void StyleDataGridView(DataGridView dgv)
    {
        dgv.EnableHeadersVisualStyles = false;
        dgv.ColumnHeadersDefaultCellStyle.BackColor   = Primary;
        dgv.ColumnHeadersDefaultCellStyle.ForeColor   = TextOnPrimary;
        dgv.ColumnHeadersDefaultCellStyle.Font        = new Font("Segoe UI", 9F, FontStyle.Bold);
        dgv.ColumnHeadersDefaultCellStyle.Alignment   = DataGridViewContentAlignment.MiddleCenter;
        dgv.ColumnHeadersHeight = 32;
        dgv.DefaultCellStyle.SelectionBackColor       = Selection;
        dgv.DefaultCellStyle.SelectionForeColor       = TextPrimary;
        dgv.DefaultCellStyle.BackColor                = Surface;
        dgv.AlternatingRowsDefaultCellStyle.BackColor = Background;
        dgv.DefaultCellStyle.Padding                  = new Padding(4, 2, 4, 2);
        dgv.RowTemplate.Height  = 28;
        dgv.GridColor           = Border;
        dgv.BorderStyle         = BorderStyle.Fixed3D;
        dgv.ReadOnly            = true;
        dgv.AllowUserToAddRows  = false;
        dgv.AllowUserToDeleteRows = false;
        dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    }

    // ── 공통 컨트롤 팩토리 ───────────────────────────────────────────────────

    /// <summary>아이콘+제목+구분선으로 구성된 Shiftee 스타일 헤더를 생성합니다.</summary>
    internal static Panel MakeFormHeader(string title, string? subtitle = null,
        string iconChar = "●", Color? iconColor = null)
    {
        const int iconSize = 36;
        const int iconGap  = 10;
        var color = iconColor ?? Primary;

        var outer = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = FormTitleH,
            BackColor = Surface
        };

        // 하단 구분선
        outer.Controls.Add(new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 1,
            BackColor = Border
        });

        // 아이콘 박스 (좌측에서 Pad, 세로 중앙 정렬)
        var iconBox = new Panel
        {
            Width     = iconSize,
            Height    = iconSize,
            BackColor = color,
            Location  = new Point(Pad, (FormTitleH - iconSize) / 2)
        };
        iconBox.Controls.Add(new Label
        {
            Dock      = DockStyle.Fill,
            Text      = iconChar,
            Font      = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        });

        int textLeft = Pad + iconSize + iconGap;
        int titleTop = subtitle != null ? (FormTitleH - 40) / 2 : (FormTitleH - 20) / 2;

        var titleLbl = new Label
        {
            Text      = title,
            Font      = H2,
            ForeColor = TextPrimary,
            AutoSize  = false,
            Location  = new Point(textLeft, titleTop),
            Size      = new Size(800, 20),
            BackColor = Color.Transparent
        };

        outer.Controls.Add(iconBox);
        outer.Controls.Add(titleLbl);

        if (subtitle != null)
        {
            var subLbl = new Label
            {
                Text      = subtitle,
                Font      = Small,
                ForeColor = TextSecondary,
                AutoSize  = false,
                Location  = new Point(textLeft, titleTop + 22),
                Size      = new Size(800, 18),
                BackColor = Color.Transparent
            };
            outer.Controls.Add(subLbl);
        }

        return outer;
    }

    /// <summary>기존 MakeHeader 호출을 MakeFormHeader로 연결합니다.</summary>
    internal static Panel MakeHeader(string title)
        => MakeFormHeader(title);

    // ── 상태 배지 ────────────────────────────────────────────────────────────

    internal enum BadgeStyle { Info, Warning, Danger, Success, Gray }

    /// <summary>색상 코딩된 상태 배지 레이블을 생성합니다.</summary>
    internal static Label MakeStatusBadge(string text, BadgeStyle style = BadgeStyle.Info)
    {
        (Color bg, Color fg) = style switch
        {
            BadgeStyle.Warning => (WarningLight, Warning),
            BadgeStyle.Danger  => (DangerLight,  Danger),
            BadgeStyle.Success => (SuccessLight, Success),
            BadgeStyle.Gray    => (Color.FromArgb(237, 238, 240), TextSecondary),
            _                  => (PrimaryLight, Primary)
        };

        int textW = TextRenderer.MeasureText(text, BadgeFont).Width;
        return new Label
        {
            Text      = text,
            AutoSize  = false,
            Font      = BadgeFont,
            ForeColor = fg,
            BackColor = bg,
            Width     = textW + 16,
            Height    = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin    = new Padding(0, 0, 6, 0)
        };
    }

    /// <summary>하단 버튼 줄에 사용하는 FlowLayoutPanel을 생성합니다.</summary>
    internal static FlowLayoutPanel MakeButtonBar()
        => new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height        = BtnH + Pad + 8,
            Padding       = new Padding(Pad, 6, Pad, 0),
            BackColor     = Surface
        };
}
