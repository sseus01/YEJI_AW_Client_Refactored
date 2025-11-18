using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    static class Program
    {
        private static readonly string InfoFolder = @"C:\ProgramData\YEJI_AW";
        private static readonly string InfoFileName = "user_info.json";

        private static string GetUserInfoFilePath()
        {
            return Path.Combine(InfoFolder, InfoFileName);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string userInfoPath = GetUserInfoFilePath();

            if (!File.Exists(userInfoPath))
            {
                using var userInfoForm = new UserInfoForm();
                if (userInfoForm.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                var info = new UserInfo { Name = userInfoForm.EmployeeName, Id = userInfoForm.EmployeeId };
                Directory.CreateDirectory(InfoFolder);
                File.WriteAllText(userInfoPath, JsonSerializer.Serialize(info));
            }

            string json = File.ReadAllText(userInfoPath);
            var userInfo = JsonSerializer.Deserialize<UserInfo>(json);

            if (userInfo == null || string.IsNullOrWhiteSpace(userInfo.Name) || string.IsNullOrWhiteSpace(userInfo.Id))
            {
                MessageBox.Show("사용자 정보를 제대로 불러오지 못했습니다.");
                return;
            }

            Application.Run(new Form1(userInfo.Name, userInfo.Id));
        }
    }
}
