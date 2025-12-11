using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
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
#if DEBUG
            // 디버그 시에는 실제 배포본과 격리된 뮤텍스 이름을 사용해
            // 이미 동작 중인 프로세스가 있어도 별도로 실행/디버깅할 수 있도록 한다.
            const string MutexName = @"Global\YEJI_AW_Client_DEBUG";
#else
            const string MutexName = @"Global\YEJI_AW_Client";
#endif

            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                ClientLogger.LogAgent("Application start blocked: duplicate instance detected.", "DBG");
#if DEBUG
                MessageBox.Show("이미 실행 중인 디버그 인스턴스가 있어 새로 실행하지 않습니다.");
#else
                MessageBox.Show("프로그램이 실행중입니다.");
#endif
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ClientLogger.LogAgent("Application initialization sequence started.");

            string userInfoPath = GetUserInfoFilePath();

            if (!File.Exists(userInfoPath))
            {
                ClientLogger.LogAgent("User info not found. Prompting for initial registration.", "DBG");
                using var userInfoForm = new UserInfoForm();
                if (userInfoForm.ShowDialog() != DialogResult.OK)
                {
                    ClientLogger.LogAgent("User cancelled initial registration. Application will exit.", "DBG");
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
                ClientLogger.LogAgent("Invalid user info detected during startup.", "Err");
                MessageBox.Show("사용자 정보를 제대로 불러오지 못했습니다.");
                return;
            }

            try
            {
                ClientLogger.LogAgent($"Launching main form for {userInfo.Id} ({userInfo.Name}).");
                Application.Run(new Form1(userInfo.Name, userInfo.Id));
            }
            finally
            {
                mutex.ReleaseMutex();
                ClientLogger.LogAgent("Application shutdown sequence completed.");
            }
        }
    }
}
