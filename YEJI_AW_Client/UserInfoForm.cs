using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class UserInfoForm : Form
    {
        private readonly string InfoFolder   = @"C:\ProgramData\YEJI_AW";
        private readonly string InfoFileName = "user_info.json";

        private readonly System.Windows.Forms.TextBox textBoxName;
        private readonly System.Windows.Forms.TextBox textBoxId;

        public string EmployeeName { get; private set; } = "";
        public string EmployeeId   { get; private set; } = "";

        public UserInfoForm()
        {
            Text            = "사용자 정보 입력";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterScreen;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ClientSize      = new System.Drawing.Size(380, 260);
            BackColor       = UiTheme.Background;
            ShowInTaskbar   = false;

            var body = new System.Windows.Forms.Panel
            {
                Dock      = System.Windows.Forms.DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Padding   = new System.Windows.Forms.Padding(UiTheme.Pad, UiTheme.Pad, UiTheme.Pad, 0)
            };

            var tbl = new System.Windows.Forms.TableLayoutPanel
            {
                Dock        = System.Windows.Forms.DockStyle.Fill,
                ColumnCount = 2,
                RowCount    = 2
            };
            tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 60));
            tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52));
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52));

            var lblName = new System.Windows.Forms.Label { Text = "이름", Font = UiTheme.Small, ForeColor = UiTheme.TextSecondary, Dock = System.Windows.Forms.DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            textBoxName = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill, Font = UiTheme.Body };

            var lblId = new System.Windows.Forms.Label { Text = "사번", Font = UiTheme.Small, ForeColor = UiTheme.TextSecondary, Dock = System.Windows.Forms.DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            textBoxId = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill, Font = UiTheme.Body, MaxLength = 6 };
            textBoxId.KeyPress += TextBoxId_KeyPress;

            tbl.Controls.Add(lblName,    0, 0);
            tbl.Controls.Add(textBoxName, 1, 0);
            tbl.Controls.Add(lblId,      0, 1);
            tbl.Controls.Add(textBoxId,  1, 1);

            var btnPanel = UiTheme.MakeButtonBar();
            var btnSave  = new RoundButton { Text = "저장", Width = UiTheme.BtnW };
            UiTheme.StylePrimary(btnSave);
            btnSave.Click += ButtonSave_Click;
            btnPanel.Controls.Add(btnSave);

            body.Controls.Add(btnPanel);
            body.Controls.Add(tbl);

            Controls.Add(body);
            Controls.Add(UiTheme.MakeFormHeader("사용자 정보", null, "●", UiTheme.Primary));

            AcceptButton = btnSave;
            LoadUserInfo();
        }

        public void SetUserInfo(string name, string id)
        {
            textBoxName.Text = name;
            textBoxId.Text   = id;
        }

        private string GetUserInfoFilePath() => Path.Combine(InfoFolder, InfoFileName);

        private void LoadUserInfo()
        {
            var fullPath = GetUserInfoFilePath();
            if (!File.Exists(fullPath)) return;
            try
            {
                var info = JsonSerializer.Deserialize<UserInfo>(File.ReadAllText(fullPath));
                if (info != null)
                {
                    textBoxName.Text = info.Name;
                    textBoxId.Text   = info.Id;
                }
            }
            catch { }
        }

        private void ButtonSave_Click(object? sender, EventArgs e)
        {
            string name = textBoxName.Text.Trim();
            string id   = textBoxId.Text.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id))
            {
                MessageBox.Show("이름과 사번을 모두 입력하세요.");
                return;
            }

            if (id.Length != 6 || !id.All(char.IsDigit))
            {
                MessageBox.Show("사번은 6자리 숫자로 입력해야 합니다.");
                return;
            }

            var info = new UserInfo { Name = name, Id = id };
            try
            {
                Directory.CreateDirectory(InfoFolder);
                File.WriteAllText(GetUserInfoFilePath(), JsonSerializer.Serialize(info));
                MessageBox.Show("사용자 정보를 저장했습니다.");
                EmployeeName = name;
                EmployeeId   = id;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("사용자 정보 저장 중 오류가 발생했습니다: " + ex.Message);
            }
        }

        private void TextBoxId_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                e.Handled = true;
        }
    }
}
