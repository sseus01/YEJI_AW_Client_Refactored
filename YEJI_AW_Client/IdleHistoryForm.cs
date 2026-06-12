using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class IdleHistoryForm : Form
    {
        private readonly DataGridView                                     _dgv;
        private readonly Label                                            _countLbl;
        private readonly DateTimePicker                                   _pickerFrom;
        private readonly DateTimePicker                                   _pickerTo;
        private readonly Func<DateTime, DateTime, Task<List<IdleEventData>>>? _fetchCallback;

        private const int ColStart   = 0;
        private const int ColEnd     = 1;
        private const int ColElapsed = 2;
        private const int ColReason  = 3;

        public IdleHistoryForm(
            List<IdleEventData> history,
            DateTime initialFrom,
            DateTime initialTo,
            Func<DateTime, DateTime, Task<List<IdleEventData>>>? fetchCallback,
            bool hasHistoryViewPermission = false,
            Action? onViewOrgHistory = null)
        {
            Text            = "자리비움 이력 확인";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            ClientSize      = new Size(820, 520);
            BackColor       = UiTheme.Background;
            ShowInTaskbar   = false;

            _fetchCallback = fetchCallback;

            // 날짜 피커를 먼저 초기화 (헤더 lambda에서 캡처하기 전에 할당)
            _pickerFrom = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = initialFrom, Width = 120 };
            _pickerTo   = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = initialTo,   Width = 120 };

            // ── 커스텀 헤더 ─────────────────────────────────────────
            const int iconSize = 36;
            var header = new Panel { Dock = DockStyle.Top, Height = UiTheme.FormTitleH, BackColor = UiTheme.Surface };
            header.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });

            var iconBox = new Panel
            {
                Width     = iconSize,
                Height    = iconSize,
                BackColor = UiTheme.Primary,
                Location  = new Point(UiTheme.Pad, (UiTheme.FormTitleH - iconSize) / 2)
            };
            iconBox.Controls.Add(new Label
            {
                Dock      = DockStyle.Fill,
                Text      = "◷",
                Font      = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            });

            int textLeft = UiTheme.Pad + iconSize + 10;
            var titleLbl = new Label
            {
                Text      = "자리비움 이력 확인",
                Font      = UiTheme.H2,
                ForeColor = UiTheme.TextPrimary,
                AutoSize  = false,
                Location  = new Point(textLeft, 14),
                Size      = new Size(300, 20),
                BackColor = Color.Transparent
            };

            _countLbl = new Label
            {
                Text      = "",
                Font      = UiTheme.Small,
                ForeColor = UiTheme.TextSecondary,
                AutoSize  = true,
                Location  = new Point(textLeft, 36),
                BackColor = Color.Transparent
            };

            // 헤더 우측 버튼들
            var headerBtnFlow = new FlowLayoutPanel
            {
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents  = false,
                Anchor        = AnchorStyles.Right | AnchorStyles.Top,
                Padding       = new Padding(0)
            };

            var btnToday = new RoundButton { Text = "오늘", Width = 60, Height = 28 };
            UiTheme.StyleOutline(btnToday);
            btnToday.Height = 28;
            btnToday.Click += async (_, _) =>
            {
                var today = DateTime.Today;
                _pickerFrom.Value = today;
                _pickerTo.Value   = today;
                await RefreshAsync(today, today);
            };
            headerBtnFlow.Controls.Add(btnToday);

            if (hasHistoryViewPermission && onViewOrgHistory != null)
            {
                var btnOrg = new RoundButton { Text = "조직 이력", Width = 80, Height = 28 };
                UiTheme.StylePrimary(btnOrg);
                btnOrg.BackColor = UiTheme.PrimaryDark;
                btnOrg.Height    = 28;
                btnOrg.Click    += (_, _) => onViewOrgHistory();
                headerBtnFlow.Controls.Add(btnOrg);
            }

            header.Controls.Add(iconBox);
            header.Controls.Add(titleLbl);
            header.Controls.Add(_countLbl);
            header.Controls.Add(headerBtnFlow);
            header.Layout += (_, _) => headerBtnFlow.Location = new Point(
                header.ClientSize.Width - headerBtnFlow.Width - UiTheme.Pad,
                (UiTheme.FormTitleH - headerBtnFlow.Height) / 2);

            // ── 날짜 범위 바 ─────────────────────────────────────────
            var dateBar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 52,
                BackColor = UiTheme.Surface,
                Padding   = new Padding(UiTheme.Pad, 8, UiTheme.Pad, 8)
            };
            dateBar.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });

            _pickerFrom.Location = new Point(UiTheme.Pad, 12);
            var tildeLabel = new Label
            {
                Text      = "~",
                AutoSize  = true,
                Location  = new Point(UiTheme.Pad + 128, 15),
                Font      = UiTheme.Body,
                ForeColor = UiTheme.TextSecondary
            };
            _pickerTo.Location = new Point(UiTheme.Pad + 148, 12);

            var btnSearch = new RoundButton { Text = "조회", Width = 72, Height = 28, Location = new Point(UiTheme.Pad + 280, 10) };
            UiTheme.StylePrimary(btnSearch);
            btnSearch.Height = 28;
            btnSearch.Click += async (_, _) => await RefreshAsync(_pickerFrom.Value, _pickerTo.Value);

            dateBar.Controls.Add(_pickerFrom);
            dateBar.Controls.Add(tildeLabel);
            dateBar.Controls.Add(_pickerTo);
            dateBar.Controls.Add(btnSearch);

            // ── DataGridView ─────────────────────────────────────────
            _dgv = new DataGridView
            {
                Dock                = DockStyle.Fill,
                AutoGenerateColumns = false
            };
            UiTheme.StyleDataGridView(_dgv);
            BuildColumns();
            _dgv.CellPainting += DgvCellPainting;

            Controls.Add(_dgv);
            Controls.Add(dateBar);
            Controls.Add(header);

            // 초기 데이터 로드
            LoadData(history);

            StartPosition = FormStartPosition.Manual;
            const int margin = 10;
            Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
            Location = new Point(
                Math.Max(margin, wa.Right  - Width  - margin),
                Math.Max(margin, wa.Bottom - Height - margin));
        }

        private void BuildColumns()
        {
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name            = "StartTime",
                HeaderText      = "시작 시각",
                Width           = 100,
                DataPropertyName = "StartTime"
            });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name            = "EndTime",
                HeaderText      = "종료 시각",
                Width           = 100,
                DataPropertyName = "EndTime"
            });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name            = "ElapsedMin",
                HeaderText      = "경과 시간",
                Width           = 85,
                DataPropertyName = "ElapsedMin"
            });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name             = "Reason",
                HeaderText       = "사유",
                AutoSizeMode     = DataGridViewAutoSizeColumnMode.Fill,
                DataPropertyName = "Reason"
            });
            foreach (DataGridViewColumn col in _dgv.Columns)
            {
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            _dgv.Columns["Reason"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        }

        private void LoadData(List<IdleEventData> history)
        {
            // 날짜 범위가 하루를 넘으면 날짜도 표시
            bool multiDay = _pickerFrom.Value.Date != _pickerTo.Value.Date;
            string fmt    = multiDay ? "M/d HH:mm" : "HH:mm";

            var rows = history.ConvertAll(item =>
            {
                DateTime start = DateTime.TryParse(item.IdleStartTime, out var s) ? s : DateTime.MinValue;
                DateTime end   = DateTime.TryParse(item.IdleEndTime,   out var e) ? e : DateTime.MinValue;
                bool ongoing   = end == DateTime.MinValue || string.IsNullOrEmpty(item.IdleEndTime);

                string reason = !string.IsNullOrEmpty(item.ReasonLevel3)
                    ? item.ReasonLevel3
                    : !string.IsNullOrEmpty(item.ReasonDetail)
                        ? item.ReasonDetail
                        : item.ReasonLevel2 ?? item.ReasonCategory ?? "";

                return new HistoryRow(
                    StartTime  : start == DateTime.MinValue ? item.IdleStartTime : start.ToString(fmt),
                    EndTime    : ongoing ? "--" : end.ToString(fmt),
                    ElapsedMin : ongoing ? "" : $"{(int)(end - start).TotalMinutes}분",
                    Reason     : reason
                );
            });

            _dgv.DataSource = rows;

            bool isToday = _pickerFrom.Value.Date == DateTime.Today && _pickerTo.Value.Date == DateTime.Today;
            _countLbl.Text = isToday
                ? $"오늘 기준 · 총 {rows.Count}건"
                : $"{_pickerFrom.Value:yyyy-MM-dd} ~ {_pickerTo.Value:yyyy-MM-dd} · 총 {rows.Count}건";
        }

        private async Task RefreshAsync(DateTime from, DateTime to)
        {
            if (_fetchCallback == null) return;
            var data = await _fetchCallback(from, to);
            LoadData(data);
        }

        private void DgvCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex != ColElapsed) return;

            string? text = e.Value?.ToString();
            if (string.IsNullOrEmpty(text)) return;

            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);

            Color bg = UiTheme.PrimaryLight, fg = UiTheme.Primary;

            var sz  = TextRenderer.MeasureText(e.Graphics, text, UiTheme.BadgeFont);
            int bw  = sz.Width + 12;
            int bh  = Math.Min(sz.Height + 6, e.CellBounds.Height - 6);
            int bx  = e.CellBounds.X + (e.CellBounds.Width  - bw) / 2;
            int by  = e.CellBounds.Y + (e.CellBounds.Height - bh) / 2;

            using (var brush = new SolidBrush(bg))
                e.Graphics.FillRectangle(brush, bx, by, bw, bh);

            TextRenderer.DrawText(
                e.Graphics, text, UiTheme.BadgeFont,
                new Rectangle(bx, by, bw, bh), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            e.Handled = true;
        }

        // DataGridView 바인딩용 레코드
        private sealed record HistoryRow(
            string StartTime,
            string EndTime,
            string ElapsedMin,
            string Reason);
    }
}
