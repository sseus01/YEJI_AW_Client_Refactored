using System;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class IdleReasonForm : Form
    {
        private bool isSaved = false;

        public string? SelectedReason { get; private set; }
        public string? DetailReason { get; private set; }

        public IdleReasonForm(DateTime idleStartTime, DateTime idleEndTime)
        {
            InitializeComponent();

            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;

            comboBoxReason.DropDownStyle = ComboBoxStyle.DropDownList;

            comboBoxReason.Items.Clear();
            comboBoxReason.Items.Add("미팅 및 보고");
            comboBoxReason.Items.Add("개인 사유");
            comboBoxReason.Items.Add("기타");

            comboBoxReason.SelectedIndexChanged += ComboBoxReason_SelectedIndexChanged;

            comboBoxReason.SelectedIndex = 0; // 디폴트 선택은 미팅 및 보고
            textBoxDetail.Enabled = true; // 창 뜰 때는 텍스트박스 활성화

            labelIdleTime.Text = $"{idleStartTime:HH:mm} ~ {idleEndTime:HH:mm}";

            this.FormClosing += IdleReasonForm_FormClosing;

            buttonSave.Click += ButtonSave_Click;
        }

        private void ComboBoxReason_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string selected = comboBoxReason.SelectedItem?.ToString() ?? string.Empty;

            if (selected == "개인 사유")
            {
                textBoxDetail.Enabled = false;
                textBoxDetail.Text = string.Empty;
            }
            else
            {
                textBoxDetail.Enabled = true;
            }
        }

        // 수정 후
        private void ButtonSave_Click(object? sender, EventArgs e)
        {
            string selectedReason = comboBoxReason.SelectedItem?.ToString() ?? string.Empty;

            if (string.IsNullOrEmpty(selectedReason))
            {
                MessageBox.Show("사유를 선택하세요.");
                return;
            }

            // 개인 사유일 경우 텍스트박스 입력은 비활성화되어 있으니 체크 제외
            if (selectedReason != "개인 사유" && string.IsNullOrWhiteSpace(textBoxDetail.Text))
            {
                MessageBox.Show("사유를 입력하세요.");
                return;
            }

            SelectedReason = selectedReason;
            DetailReason = textBoxDetail.Enabled ? textBoxDetail.Text.Trim() : string.Empty;

            isSaved = true;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }


        private void IdleReasonForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!isSaved)
            {
                MessageBox.Show("사유를 선택 및 입력 후 저장해야 합니다.");
                e.Cancel = true;
            }
        }
    }
}
