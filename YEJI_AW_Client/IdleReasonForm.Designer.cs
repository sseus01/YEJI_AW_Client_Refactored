using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    partial class IdleReasonForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            labelInstruction        = new Label();
            comboBoxLevel1          = new ComboBox();
            comboBoxLevel2          = new ComboBox();
            labelLevel3Value        = new Label();
            textBoxDetail           = new TextBox();
            buttonSave              = new RoundButton();
            buttonShowAll           = new RoundButton();
            labelIdleTime           = new Label();
            label1                  = new Label();
            labelLevel1             = new Label();
            labelLevel2             = new Label();
            labelLevel3             = new Label();
            labelDetailInstruction  = new Label();
            _durationBadge          = new Label();

            SuspendLayout();

            // ── 헤더 ────────────────────────────────────────────────
            var header = UiTheme.MakeFormHeader(
                "자리비움 사유 입력",
                "자리를 비운 시간을 기록해주세요",
                "◎", UiTheme.Primary);

            // ── 버튼 바 (하단) ───────────────────────────────────────
            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = UiTheme.Surface };
            btnPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border });

            buttonSave.Text   = "저장";
            buttonSave.Width  = 100;
            buttonSave.Height = UiTheme.BtnH;
            UiTheme.StylePrimary(buttonSave);

            buttonShowAll.Text   = "전체 목록";
            buttonShowAll.Width  = 110;
            buttonShowAll.Height = UiTheme.BtnH;
            UiTheme.StyleOutline(buttonShowAll);

            var btnFlow = new FlowLayoutPanel
            {
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents  = false,
                Anchor        = AnchorStyles.Right | AnchorStyles.Top,
                Padding       = new Padding(0)
            };
            btnFlow.Controls.Add(buttonSave);
            btnFlow.Controls.Add(buttonShowAll);
            btnPanel.Controls.Add(btnFlow);
            btnPanel.Layout += (_, _) => btnFlow.Location = new Point(
                btnPanel.ClientSize.Width - btnFlow.Width - UiTheme.Pad,
                (btnPanel.ClientSize.Height - btnFlow.Height) / 2 + 2);

            // ── 본문 ─────────────────────────────────────────────────
            var body = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = UiTheme.Background,
                Padding   = new Padding(UiTheme.Pad, 12, UiTheme.Pad, 8)
            };

            // ── 1) 시간 배지 행 ──────────────────────────────────────
            var timeBadgesPanel = new FlowLayoutPanel
            {
                Dock          = DockStyle.Top,
                Height        = 40,
                BackColor     = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Padding       = new Padding(0, 4, 0, 4)
            };

            labelIdleTime.AutoSize  = false;
            labelIdleTime.Font      = UiTheme.BadgeFont;
            labelIdleTime.ForeColor = UiTheme.Primary;
            labelIdleTime.BackColor = UiTheme.PrimaryLight;
            labelIdleTime.Size      = new Size(130, 24);
            labelIdleTime.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            labelIdleTime.Text      = "--:-- ~ --:--";
            labelIdleTime.Margin    = new Padding(0, 0, 6, 0);

            _durationBadge.AutoSize  = false;
            _durationBadge.Font      = UiTheme.BadgeFont;
            _durationBadge.ForeColor = UiTheme.TextSecondary;
            _durationBadge.BackColor = Color.FromArgb(237, 238, 240);
            _durationBadge.Size      = new Size(50, 24);
            _durationBadge.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _durationBadge.Text      = "--분";

            timeBadgesPanel.Controls.Add(labelIdleTime);
            timeBadgesPanel.Controls.Add(_durationBadge);

            // ── 2) 드롭다운 행 (구분 / 세부유형) ────────────────────
            var dropdownPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 62,
                BackColor = Color.Transparent
            };

            labelLevel1.Text      = "구분";
            labelLevel1.Font      = UiTheme.Small;
            labelLevel1.ForeColor = UiTheme.TextSecondary;
            labelLevel1.AutoSize  = true;
            labelLevel1.Location  = new Point(0, 2);

            comboBoxLevel1.Location          = new Point(0, 20);
            comboBoxLevel1.Size              = new Size(165, 28);
            comboBoxLevel1.DropDownStyle     = ComboBoxStyle.DropDownList;
            comboBoxLevel1.FormattingEnabled = true;

            labelLevel2.Text      = "세부유형";
            labelLevel2.Font      = UiTheme.Small;
            labelLevel2.ForeColor = UiTheme.TextSecondary;
            labelLevel2.AutoSize  = true;
            labelLevel2.Location  = new Point(177, 2);

            comboBoxLevel2.Location          = new Point(177, 20);
            comboBoxLevel2.Size              = new Size(165, 28);
            comboBoxLevel2.DropDownStyle     = ComboBoxStyle.DropDownList;
            comboBoxLevel2.FormattingEnabled = true;

            // labelInstruction, label1 은 새 UI에서 사용하지 않음 (숨김)
            labelInstruction.Visible = false;
            label1.Visible           = false;

            dropdownPanel.Controls.Add(labelLevel1);
            dropdownPanel.Controls.Add(comboBoxLevel1);
            dropdownPanel.Controls.Add(labelLevel2);
            dropdownPanel.Controls.Add(comboBoxLevel2);

            // ── 3) 예시 항목 행 ──────────────────────────────────────
            var examplePanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 52,
                BackColor = Color.Transparent
            };

            labelLevel3.Text      = "예시 항목";
            labelLevel3.Font      = UiTheme.Small;
            labelLevel3.ForeColor = UiTheme.TextSecondary;
            labelLevel3.AutoSize  = true;
            labelLevel3.Location  = new Point(0, 2);

            labelLevel3Value.Font        = UiTheme.Body;
            labelLevel3Value.ForeColor   = UiTheme.TextPrimary;
            labelLevel3Value.BackColor   = UiTheme.Background;
            labelLevel3Value.AutoSize    = false;
            labelLevel3Value.Location    = new Point(0, 20);
            labelLevel3Value.Size        = new Size(528, 26);
            labelLevel3Value.Padding     = new Padding(8, 0, 0, 0);
            labelLevel3Value.BorderStyle = BorderStyle.FixedSingle;
            labelLevel3Value.TextAlign   = System.Drawing.ContentAlignment.MiddleLeft;

            examplePanel.Controls.Add(labelLevel3);
            examplePanel.Controls.Add(labelLevel3Value);

            // ── 4) 상세 사유 레이블 ──────────────────────────────────
            labelDetailInstruction.Text      = "상세 사유";
            labelDetailInstruction.Font      = UiTheme.H3;
            labelDetailInstruction.ForeColor = UiTheme.TextPrimary;
            labelDetailInstruction.BackColor = Color.Transparent;
            labelDetailInstruction.Dock      = DockStyle.Top;
            labelDetailInstruction.Height    = 28;
            labelDetailInstruction.Padding   = new Padding(0, 6, 0, 0);

            // ── 5) 상세 사유 입력 (Fill) ─────────────────────────────
            textBoxDetail.Dock        = DockStyle.Fill;
            textBoxDetail.Multiline   = true;
            textBoxDetail.ScrollBars  = ScrollBars.Vertical;
            textBoxDetail.Font        = UiTheme.Body;
            textBoxDetail.BackColor   = UiTheme.Surface;
            textBoxDetail.BorderStyle = BorderStyle.FixedSingle;

            // body에 추가 (Fill 먼저, 이후 Dock.Top은 나중에 추가될수록 위에)
            body.Controls.Add(textBoxDetail);
            body.Controls.Add(labelDetailInstruction);
            body.Controls.Add(examplePanel);
            body.Controls.Add(dropdownPanel);
            body.Controls.Add(timeBadgesPanel);

            // ── 폼 조립 ──────────────────────────────────────────────
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode       = AutoScaleMode.Font;
            ClientSize          = new System.Drawing.Size(560, 450);
            FormBorderStyle     = FormBorderStyle.FixedDialog;
            MinimizeBox         = false;
            MaximizeBox         = false;
            Name                = "IdleReasonForm";
            Text                = "자리비움 사유 입력";

            Controls.Add(body);
            Controls.Add(btnPanel);
            Controls.Add(header);

            ResumeLayout(false);
            PerformLayout();
        }

        private Label       labelInstruction;
        private ComboBox    comboBoxLevel1;
        private ComboBox    comboBoxLevel2;
        private TextBox     textBoxDetail;
        private RoundButton buttonSave;
        private Label       labelIdleTime;
        private Label       label1;
        private Label       labelLevel1;
        private Label       labelLevel2;
        private Label       labelLevel3;
        private Label       labelDetailInstruction;
        private Label       labelLevel3Value;
        private RoundButton buttonShowAll;
        private Label       _durationBadge;
    }
}
