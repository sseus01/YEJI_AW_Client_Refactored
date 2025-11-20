using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class IdleHistoryForm : Form
    {
        public IdleHistoryForm(List<IdleEventData> history)
        {
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

            StartPosition = FormStartPosition.Manual;
            const int margin = 10;
            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
            Location = new Point(
                Math.Max(margin, workingArea.Right - Width - margin),
                Math.Max(margin, workingArea.Bottom - Height - margin));
        }
    }
}
