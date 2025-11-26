using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class UserInfoForm : Form
    {
        private readonly string InfoFolder = @"C:\ProgramData\YEJI_AW";
        private readonly string InfoFileName = "user_info.json";

        public string EmployeeName { get; private set; } = "";
        public string EmployeeId { get; private set; } = "";

        public UserInfoForm()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterScreen;
            LoadUserInfo();
        }

        public void SetUserInfo(string name, string id)
        {
            textBoxName.Text = name;
            textBoxId.Text = id;
        }

        private string GetUserInfoFilePath()
        {
            return Path.Combine(InfoFolder, InfoFileName);
        }

        private void LoadUserInfo()
        {
            var fullPath = GetUserInfoFilePath();
            if (File.Exists(fullPath))
            {
                try
                {
                    var info = JsonSerializer.Deserialize<UserInfo>(File.ReadAllText(fullPath));
                    if (info != null)
                    {
                        textBoxName.Text = info.Name;
                        textBoxId.Text = info.Id;
                    }
                }
                catch
                {
                    // 예외 무시
                }
            }
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            string name = textBoxName.Text.Trim();
            string id = textBoxId.Text.Trim();

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
                if (!Directory.Exists(InfoFolder))
                {
                    Directory.CreateDirectory(InfoFolder);
                }
                File.WriteAllText(GetUserInfoFilePath(), JsonSerializer.Serialize(info));
                MessageBox.Show("사용자 정보를 저장했습니다.");

                EmployeeName = name;
                EmployeeId = id;

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("사용자 정보 저장 중 오류가 발생했습니다: " + ex.Message);
            }
        }
        private void textBoxId_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }
    }
}
