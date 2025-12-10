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

            // 이 한 줄로 모든 셀 너비 자동 조정!
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;

            // DataGridView 스타일 개선
            ImproveDataGridViewStyle();

            // 관리자 권한이 있는 경우 조직 이력 보기 버튼 추가
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
        }
        private void ImproveDataGridViewStyle()
        {
            // 헤더 스타일
            dataGridView1.EnableHeadersVisualStyles = false;
            dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(107, 114, 128);
            dataGridView1.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            headerFont = new Font(dataGridView1.Font.FontFamily, 9F, FontStyle.Bold);
            dataGridView1.ColumnHeadersDefaultCellStyle.Font = headerFont;
            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridView1.ColumnHeadersHeight = 32;

            // 셀 스타일
            dataGridView1.DefaultCellStyle.SelectionBackColor = Color.FromArgb(229, 231, 235);
            dataGridView1.DefaultCellStyle.SelectionForeColor = Color.FromArgb(17, 24, 39);
            dataGridView1.DefaultCellStyle.BackColor = Color.White;
            dataGridView1.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251);
            dataGridView1.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            dataGridView1.RowTemplate.Height = 28;

            // 그리드 라인
            dataGridView1.GridColor = Color.FromArgb(229, 231, 235);
            dataGridView1.BorderStyle = BorderStyle.Fixed3D;

            // 읽기 전용
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
                BackColor = Color.FromArgb(107, 114, 128),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            buttonFont = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            btnOrgHistory.Font = buttonFont;
            btnOrgHistory.FlatAppearance.BorderSize = 0;
            btnOrgHistory.FlatAppearance.MouseOverBackColor = Color.FromArgb(156, 163, 175);

            // 버튼을 폼의 우측 상단에 배치
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
