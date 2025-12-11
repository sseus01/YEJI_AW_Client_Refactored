using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class IdleHistoryForm : Form
    {
        private readonly bool isManager;
        private readonly Action? onViewOrgHistory;
        private Font? headerFont;
        private Font? buttonFont;

        public IdleHistoryForm(List<IdleEventData> history, bool isManager = false, Action? onViewOrgHistory = null)
        {
            this.isManager = isManager;
            this.onViewOrgHistory = onViewOrgHistory;
            InitializeComponent();

            var displayList = history.ConvertAll(item => {
                DateTime start = DateTime.TryParse(item.IdleStartTime, out var s) ? s : DateTime.MinValue;
                DateTime end = DateTime.TryParse(item.IdleEndTime, out var e) ? e : DateTime.MinValue;
                return new
                {
                    이름 = item.EmployeeName,
                    사번 = item.EmployeeId,
                    시작시간 = start == DateTime.MinValue ? item.IdleStartTime : start.ToString("yyyy-MM-dd HH:mm:ss"),
                    종료시간 = end == DateTime.MinValue ? item.IdleEndTime : end.ToString("yyyy-MM-dd HH:mm:ss"),
                    자리비움시간 = (end > start ? (end - start).ToString(@"hh\:mm\:ss") : ""),
                    사유코드 = item.ReasonCode ?? "",
                    구분 = item.ReasonLevel1 ?? item.ReasonCategory ?? "",
                    세부유형 = item.ReasonLevel2 ?? "",
                    상세사유 = item.ReasonDetail ?? ""
                };
            });

            dataGridView1.AutoGenerateColumns = true;
            dataGridView1.DataSource = displayList;
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;

            ImproveDataGridViewStyle();

            if (isManager && onViewOrgHistory != null)
            {
                AddOrgHistoryButton();
            }

            StartPosition = FormStartPosition.Manual;
            const int margin = 10;
            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
            Location = new Point(
                Math.Max(margin, workingArea.Right - Width - margin),
                Math.Max(margin, workingArea.Bottom - Height - margin));
            
            BackColor = Color.FromArgb(247, 247, 247); // 전체 창 기본 배경
        }

        private void ImproveDataGridViewStyle()
        {
            dataGridView1.EnableHeadersVisualStyles = false;
            dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(7, 87, 167); // 헤더 파랑
            dataGridView1.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            headerFont = new Font(dataGridView1.Font.FontFamily, 9F, FontStyle.Bold);
            dataGridView1.ColumnHeadersDefaultCellStyle.Font = headerFont;
            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridView1.ColumnHeadersHeight = 32;

            dataGridView1.DefaultCellStyle.SelectionBackColor = Color.FromArgb(213, 220, 228); // 선택 배경
            dataGridView1.DefaultCellStyle.SelectionForeColor = Color.Black;
            dataGridView1.DefaultCellStyle.BackColor = Color.White;
            dataGridView1.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251); // 행 배경
            dataGridView1.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            dataGridView1.RowTemplate.Height = 28;

            dataGridView1.GridColor = Color.FromArgb(231, 231, 231); // 테이블 라인
            dataGridView1.BorderStyle = BorderStyle.Fixed3D;

            dataGridView1.ReadOnly = true;
            dataGridView1.AllowUserToAddRows = false;
            dataGridView1.AllowUserToDeleteRows = false;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        }

        private void AddOrgHistoryButton()
        {
            var btnOrgHistory = new Button
            {
                Text = "조직 이력 보기",
                Width = 120,
                Height = 30,
                BackColor = Color.FromArgb(7, 87, 167), // 버튼 파랑
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            buttonFont = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            btnOrgHistory.Font = buttonFont;
            btnOrgHistory.FlatAppearance.BorderSize = 0;
            btnOrgHistory.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 110, 190);

            btnOrgHistory.Location = new Point(ClientSize.Width - btnOrgHistory.Width - 15, 12);
            btnOrgHistory.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            btnOrgHistory.Click += (s, e) =>
            {
                onViewOrgHistory?.Invoke();
            };

            Controls.Add(btnOrgHistory);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                headerFont?.Dispose();
                buttonFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
