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
        private readonly Button buttonSelect;
        private readonly Button buttonCancel;

        public AwayReason? SelectedReason { get; private set; }

        public ReasonSelectionForm(IEnumerable<AwayReason> reasons, AwayReason? currentSelection)
        {
            Text = "사유 전체보기";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(520, 400);

            listViewReasons = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Dock = DockStyle.Top,
                Height = 300
            };

            listViewReasons.Columns.Add("구분", 120, HorizontalAlignment.Left);
            listViewReasons.Columns.Add("세부유형", 120, HorizontalAlignment.Left);
            listViewReasons.Columns.Add("예시", 200, HorizontalAlignment.Left);

            PopulateList(reasons, currentSelection);

            buttonSelect = new Button
            {
                Text = "선택",
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(320, 320),
                Size = new Size(80, 30)
            };

            buttonCancel = new Button
            {
                Text = "취소",
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(410, 320),
                Size = new Size(80, 30)
            };

            Controls.Add(listViewReasons);
            Controls.Add(buttonSelect);
            Controls.Add(buttonCancel);

            listViewReasons.ItemActivate += (_, _) => ConfirmSelection();
            buttonSelect.Click += (_, _) => ConfirmSelection();
        }

        public void PositionNextToOwner(Form owner)
        {
            StartPosition = FormStartPosition.Manual;

            var screen = Screen.FromControl(owner).WorkingArea;

            int x = owner.Right + 10;
            if (x + Width > screen.Right)
            {
                x = owner.Left - Width - 10;
            }

            int y = owner.Top;
            if (y + Height > screen.Bottom)
            {
                y = screen.Bottom - Height - 10;
            }

            if (y < screen.Top)
            {
                y = screen.Top + 10;
            }

            Location = new Point(x, y);
            TopMost = owner.TopMost;
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
                    if (item.Tag is AwayReason reason &&
                        reason.Level1 == currentSelection.Level1 &&
                        reason.Level2 == currentSelection.Level2 &&
                        reason.Level3 == currentSelection.Level3)
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
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}