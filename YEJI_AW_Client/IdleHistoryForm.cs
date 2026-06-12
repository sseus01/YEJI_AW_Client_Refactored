using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class IdleHistoryForm : Form
    {
        public IdleHistoryForm(List<IdleEventData> history, bool hasHistoryViewPermission = false, Action? onViewOrgHistory = null)
        {
            Text            = "자리비움 이력 확인";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            ClientSize      = new Size(1000, 500);
            BackColor       = UiTheme.Background;
            ShowInTaskbar   = false;

            // ── 헤더 (선택적 버튼 포함) ─────────────────────────────
            var header = UiTheme.MakeFormHeader("자리비움 이력 확인", null, "◷", UiTheme.Primary);
            if (hasHistoryViewPermission && onViewOrgHistory != null)
            {
                var btnOrg = new RoundButton { Text = "조직 이력 보기", Width = 110, Dock = DockStyle.Right };
                UiTheme.StylePrimary(btnOrg);
                btnOrg.BackColor = UiTheme.PrimaryDark;
                btnOrg.Click += (_, _) => onViewOrgHistory();
                header.Controls.Add(btnOrg);
            }

            // ── DataGridView ────────────────────────────────────────
            var dgv = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true };
            UiTheme.StyleDataGridView(dgv);

            var displayList = history.ConvertAll(item =>
            {
                DateTime start = DateTime.TryParse(item.IdleStartTime, out var s) ? s : DateTime.MinValue;
                DateTime end   = DateTime.TryParse(item.IdleEndTime,   out var e) ? e : DateTime.MinValue;
                return new
                {
                    이름         = item.EmployeeName,
                    사번         = item.EmployeeId,
                    시작시간    = start == DateTime.MinValue ? item.IdleStartTime : start.ToString("yyyy-MM-dd HH:mm:ss"),
                    종료시간    = end   == DateTime.MinValue ? item.IdleEndTime   : end.ToString("yyyy-MM-dd HH:mm:ss"),
                    자리비움시간 = (end > start ? (end - start).ToString(@"hh\:mm\:ss") : ""),
                    사유코드    = item.ReasonCode ?? "",
                    구분        = item.ReasonLevel1 ?? item.ReasonCategory ?? "",
                    세부유형    = item.ReasonLevel2 ?? "",
                    상세사유    = item.ReasonDetail ?? ""
                };
            });

            dgv.DataSource = displayList;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;

            Controls.Add(dgv);
            Controls.Add(header);

            StartPosition = FormStartPosition.Manual;
            const int margin = 10;
            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
            Location = new Point(
                Math.Max(margin, workingArea.Right  - Width  - margin),
                Math.Max(margin, workingArea.Bottom - Height - margin));
        }
    }
}
