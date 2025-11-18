using System;
using System.Collections.Generic;
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
                    사유 = item.ReasonCategory ?? "",
                    상세사유 = item.ReasonDetail ?? ""
                };
            });

            dataGridView1.AutoGenerateColumns = true;
            dataGridView1.DataSource = displayList;

            // 이 한 줄로 모든 셀 너비 자동 조정!
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        }
    }
}
