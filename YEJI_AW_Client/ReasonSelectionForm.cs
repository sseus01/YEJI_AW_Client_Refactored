using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class ReasonSelectionForm : Form
    {
        private readonly ListView listViewReasons;

        public AwayReason? SelectedReason { get; private set; }

        public ReasonSelectionForm(IEnumerable<AwayReason> reasons, AwayReason? currentSelection)
        {
            Text            = "사유 전체보기";
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(520, 440);
            MaximizeBox     = false;
            BackColor       = UiTheme.Background;

            // ── ListView ────────────────────────────────────────────
            listViewReasons = new ListView
            {
                View          = View.Details,
                FullRowSelect  = true,
                GridLines      = true,
                Dock           = DockStyle.Fill,
                BackColor      = UiTheme.Surface,
                BorderStyle    = BorderStyle.Fixed3D,
                Font           = UiTheme.Body
            };
            listViewReasons.Columns.Add("구분",   120);
            listViewReasons.Columns.Add("세부유형", 120);
            listViewReasons.Columns.Add("예시",   240);

            // ── 버튼 바 ─────────────────────────────────────────────
            var btnPanel = UiTheme.MakeButtonBar();

            var buttonSelect = new RoundButton { Text = "선택", Width = UiTheme.BtnW, DialogResult = DialogResult.OK };
            UiTheme.StylePrimary(buttonSelect);

            var buttonCancel = new RoundButton { Text = "취소", Width = UiTheme.BtnW, DialogResult = DialogResult.Cancel };
            UiTheme.StyleOutline(buttonCancel);

            btnPanel.Controls.Add(buttonSelect);
            btnPanel.Controls.Add(buttonCancel);

            Controls.Add(listViewReasons);
            Controls.Add(btnPanel);
            Controls.Add(UiTheme.MakeFormHeader("사유 전체보기", null, "≡", UiTheme.Primary));

            AcceptButton = buttonSelect;
            CancelButton = buttonCancel;

            PopulateList(reasons, currentSelection);

            listViewReasons.ItemActivate += (_, _) => ConfirmSelection();
            buttonSelect.Click           += (_, _) => ConfirmSelection();
        }

        public void PositionNextToOwner(Form owner)
        {
            StartPosition = FormStartPosition.Manual;
            var screen = Screen.FromControl(owner).WorkingArea;

            int x = owner.Right + 10;
            if (x + Width > screen.Right)  x = owner.Left - Width - 10;

            int y = owner.Top;
            if (y + Height > screen.Bottom) y = screen.Bottom - Height - 10;
            if (y < screen.Top)             y = screen.Top + 10;

            Location = new Point(x, y);
            TopMost  = owner.TopMost;
        }

        private void PopulateList(IEnumerable<AwayReason> reasons, AwayReason? currentSelection)
        {
            listViewReasons.Items.Clear();
            foreach (var reason in reasons)
            {
                var item = new ListViewItem(new[] { reason.Level1, reason.Level2, reason.Level3 })
                {
                    Tag = reason
                };
                listViewReasons.Items.Add(item);
            }

            if (currentSelection != null)
            {
                foreach (ListViewItem item in listViewReasons.Items)
                {
                    if (item.Tag is AwayReason r &&
                        r.Level1 == currentSelection.Level1 &&
                        r.Level2 == currentSelection.Level2 &&
                        r.Level3 == currentSelection.Level3)
                    {
                        item.Selected = true;
                        item.EnsureVisible();
                        break;
                    }
                }
            }
        }

        private void ConfirmSelection()
        {
            if (listViewReasons.SelectedItems.Count == 0)
            {
                MessageBox.Show("사유를 선택하세요.");
                DialogResult = DialogResult.None;
                return;
            }
            SelectedReason = listViewReasons.SelectedItems[0].Tag as AwayReason;
            DialogResult   = DialogResult.OK;
            Close();
        }
    }
}
