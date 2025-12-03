using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public class ManagedIdleHistoryForm : Form
    {
        private readonly string serverBaseUrl;
        private readonly HttpClient httpClient;
        private readonly string managerEmpId;

        private ComboBox orgCombo;
        private ComboBox userCombo;
        private DateTimePicker startPicker;
        private DateTimePicker endPicker;
        private Button searchButton;
        private ListView listView;

        public ManagedIdleHistoryForm(string serverBaseUrl, HttpClient httpClient, string managerEmpId)
        {
            this.serverBaseUrl = serverBaseUrl;
            this.httpClient = httpClient;
            this.managerEmpId = managerEmpId;
            InitializeUi();
            Load += async (s, e) => await LoadOrganizationsAsync();
        }

        private void InitializeUi()
        {
            Text = "관리: 조직 자리비움 이력";
            ClientSize = new Size(900, 540);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            orgCombo = new ComboBox { Left = 12, Top = 12, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            userCombo = new ComboBox { Left = 220, Top = 12, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };

            startPicker = new DateTimePicker { Left = 430, Top = 12, Width = 160, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
            endPicker = new DateTimePicker { Left = 600, Top = 12, Width = 160, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
            startPicker.Value = DateTime.Today;
            endPicker.Value = DateTime.Today;

            searchButton = new Button { Left = 770, Top = 10, Width = 110, Height = 26, Text = "조회" };
            searchButton.Click += async (s, e) => await LoadIdleEventsAsync();

            listView = new ListView { Left = 12, Top = 44, Width = 868, Height = 480, View = View.Details, FullRowSelect = true };
            listView.Columns.Add("사번", 80);
            listView.Columns.Add("이름", 120);
            listView.Columns.Add("PC", 160);
            listView.Columns.Add("시작", 200);
            listView.Columns.Add("종료", 200);
            listView.Columns.Add("사유", 200);

            Controls.Add(orgCombo);
            Controls.Add(userCombo);
            Controls.Add(startPicker);
            Controls.Add(endPicker);
            Controls.Add(searchButton);
            Controls.Add(listView);

            orgCombo.SelectedIndexChanged += async (s, e) => await LoadUsersForSelectedOrgAsync();
        }

        private async Task LoadOrganizationsAsync()
        {
            try
            {
                string url = $"{serverBaseUrl}/api/client/manager-orgs?employeeId={Uri.EscapeDataString(managerEmpId)}";
                string json = await httpClient.GetStringAsync(url);
                var orgs = JsonSerializer.Deserialize<List<OrgDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                orgCombo.Items.Clear();
                foreach (var o in orgs)
                {
                    orgCombo.Items.Add(new ComboItem(o.Name, o.Code));
                }
                if (orgCombo.Items.Count > 0) orgCombo.SelectedIndex = 0;
            }
            catch
            {
                MessageBox.Show("조직 목록을 불러오지 못했습니다.");
            }
        }

        private async Task LoadUsersForSelectedOrgAsync()
        {
            try
            {
                var selected = orgCombo.SelectedItem as ComboItem;
                if (selected == null) return;
                string url = $"{serverBaseUrl}/api/client/manager-users?orgCode={Uri.EscapeDataString(selected.Value)}";
                string json = await httpClient.GetStringAsync(url);
                var users = JsonSerializer.Deserialize<List<UserDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                userCombo.Items.Clear();
                foreach (var u in users)
                {
                    userCombo.Items.Add(new ComboItem($"{u.OrgName} / {u.DisplayName}", u.EmployeeId));
                }
                if (userCombo.Items.Count > 0) userCombo.SelectedIndex = 0;
            }
            catch
            {
                MessageBox.Show("사용자 목록을 불러오지 못했습니다.");
            }
        }

        private async Task LoadIdleEventsAsync()
        {
            try
            {
                var userItem = userCombo.SelectedItem as ComboItem;
                if (userItem == null)
                {
                    MessageBox.Show("조직과 사용자를 선택하세요.");
                    return;
                }

                var startDate = startPicker.Value.Date;
                var endDate = endPicker.Value.Date;

                string url = $"{serverBaseUrl}/api/idle-events?employeeId={Uri.EscapeDataString(userItem.Value)}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
                string json = await httpClient.GetStringAsync(url);
                var events = JsonSerializer.Deserialize<List<IdleEventData>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                listView.Items.Clear();
                foreach (var e in events)
                {
                    var item = new ListViewItem(new[]
                    {
                        e.EmployeeId,
                        e.EmployeeName,
                        e.ComputerName,
                        e.IdleStartTime,
                        e.IdleEndTime,
                        e.ReasonDetail
                    });
                    listView.Items.Add(item);
                }
            }
            catch
            {
                MessageBox.Show("자리비움 이력을 가져오지 못했습니다.");
            }
        }

        private class ComboItem
        {
            public string Text { get; }
            public string Value { get; }
            public ComboItem(string text, string value) { Text = text; Value = value; }
            public override string ToString() => Text;
        }

        private class OrgDto
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        private class UserDto
        {
            public string EmployeeId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string OrgName { get; set; } = string.Empty;
        }
    }
}
