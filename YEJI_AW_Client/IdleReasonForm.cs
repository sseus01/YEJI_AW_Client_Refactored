using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class IdleReasonForm : Form
    {
        private bool isSaved = false;

        public string? SelectedReasonCode { get; private set; }
        public string? SelectedLevel1 { get; private set; }
        public string? SelectedLevel2 { get; private set; }
        public string? SelectedLevel3 { get; private set; }
        public string? DetailReason { get; private set; }

        private readonly string serverBaseUrl;
        private List<AwayReason> awayReasons = new();

        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly object ReasonCacheLock = new();
        private static List<AwayReason> cachedAwayReasons = new();
        private static DateTime lastReasonFetchUtc = DateTime.MinValue;
        private static readonly TimeSpan ReasonCacheDuration = TimeSpan.FromMinutes(30);
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public IdleReasonForm(DateTime idleStartTime, DateTime idleEndTime, string serverBaseUrl)
        {
            InitializeComponent();

            this.serverBaseUrl = serverBaseUrl;

            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;

            comboBoxLevel1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxLevel2.DropDownStyle = ComboBoxStyle.DropDownList;
       
            comboBoxLevel1.SelectedIndexChanged += ComboBoxLevel1_SelectedIndexChanged;
            comboBoxLevel2.SelectedIndexChanged += ComboBoxLevel2_SelectedIndexChanged;         

            labelIdleTime.Text = $"{idleStartTime:HH:mm} ~ {idleEndTime:HH:mm}";

            this.FormClosing += IdleReasonForm_FormClosing;

            buttonSave.Click += ButtonSave_Click;

            this.Load += async (s, e) => await LoadReasonsAsync();
        }

        private bool TryGetCachedReasons(out List<AwayReason> reasons)
        {
            lock (ReasonCacheLock)
            {
                if (cachedAwayReasons.Count > 0 && DateTime.UtcNow - lastReasonFetchUtc <= ReasonCacheDuration)
                {
                    reasons = new List<AwayReason>(cachedAwayReasons);
                    return true;
                }
            }

            reasons = new List<AwayReason>();
            return false;
        }

        private void UpdateReasonCache(List<AwayReason> reasons)
        {
            lock (ReasonCacheLock)
            {
                cachedAwayReasons = new List<AwayReason>(reasons);
                lastReasonFetchUtc = DateTime.UtcNow;
            }
        }

        private async Task LoadReasonsAsync()
        {
            if (TryGetCachedReasons(out var cached))
            {
                awayReasons = cached;
                PopulateLevel1();
                return;
            }

            try
            {
                var client = HttpClient;
                string url = serverBaseUrl.TrimEnd('/') + "/api/away-reasons";
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                awayReasons = await JsonSerializer.DeserializeAsync<List<AwayReason>>(stream, JsonOptions)
                    ?? new List<AwayReason>();
                awayReasons = awayReasons
                   .OrderBy(r => r.ReasonCode)
                   .ToList();

                UpdateReasonCache(awayReasons);
            }
            catch
            {
                awayReasons = new List<AwayReason>
                {
                    new AwayReason
                    {
                        ReasonCode = "Z99",
                        Level1 = "기타",
                        Level2 = "기타",
                        Level3 = "기타(직접입력)",
                        IsWorkApproved = false
                    }
                };

                MessageBox.Show("사유 목록을 불러오지 못했습니다. 네트워크 상태를 확인해주세요. (기타 선택 가능)");
            }

            PopulateLevel1();
        }

        private void PopulateLevel1()
        {
            comboBoxLevel1.Items.Clear();
            var seenLevel1 = new HashSet<string>();
            foreach (var level1 in awayReasons.Select(r => r.Level1))
            {
                if (seenLevel1.Add(level1))
                {
                    comboBoxLevel1.Items.Add(level1);
                }
            }

            if (comboBoxLevel1.Items.Count > 0)
            {
                comboBoxLevel1.SelectedIndex = 0;
            }
        }

        private void PopulateLevel2(string level1)
        {
            comboBoxLevel2.Items.Clear();
            var seenLevel2 = new HashSet<string>();
            foreach (var level2 in awayReasons
               .Where(r => r.Level1 == level1)
               .Select(r => r.Level2))
            {
                if (seenLevel2.Add(level2))
                {
                    comboBoxLevel2.Items.Add(level2);
                }
            }

            if (comboBoxLevel2.Items.Count > 0)
            {
                comboBoxLevel2.SelectedIndex = 0;
            }
        }

        private void PopulateLevel3(string level1, string level2)
        {
            var level3 = awayReasons
                .Where(r => r.Level1 == level1 && r.Level2 == level2)
                .Select(r => r.Level3)
                .FirstOrDefault();

            labelLevel3Value.Text = level3 ?? string.Empty;
        }
          
        private void ComboBoxLevel1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string level1 = comboBoxLevel1.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(level1)) return;

            PopulateLevel2(level1);
            ApplyPersonalRestDetail();
        }

        private void ComboBoxLevel2_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string level1 = comboBoxLevel1.SelectedItem?.ToString() ?? string.Empty;
            string level2 = comboBoxLevel2.SelectedItem?.ToString() ?? string.Empty;

            if (string.IsNullOrEmpty(level1) || string.IsNullOrEmpty(level2)) return;

            PopulateLevel3(level1, level2);
            ApplyPersonalRestDetail();
        }

         // 수정 후
        private void ButtonSave_Click(object? sender, EventArgs e)
        {
            if (isSaved)
            {
                return;
            }

            string level1 = comboBoxLevel1.SelectedItem?.ToString() ?? string.Empty;
            string level2 = comboBoxLevel2.SelectedItem?.ToString() ?? string.Empty;
            string level3 = labelLevel3Value.Text;

            if (string.IsNullOrEmpty(level1) || string.IsNullOrEmpty(level2) || string.IsNullOrEmpty(level3))
            {
                MessageBox.Show("사유를 모두 선택하세요.");
                return;
            }

            var matchedReason = awayReasons.FirstOrDefault(r =>
               r.Level1 == level1 && r.Level2 == level2 && r.Level3 == level3);

            SelectedReasonCode = matchedReason?.ReasonCode ?? string.Empty;
            SelectedLevel1 = level1;
            SelectedLevel2 = level2;
            SelectedLevel3 = level3;
            DetailReason = textBoxDetail.Text.Trim();

            bool isRestroom = level3 == "화장실이용";

            if (isRestroom && string.IsNullOrWhiteSpace(DetailReason))
            {
                DetailReason = "화장실이용";
                textBoxDetail.Text = DetailReason;
            }
            else if (string.IsNullOrWhiteSpace(DetailReason))
            {
                MessageBox.Show("사유를 입력해주세요.");
                return;
            }

            isSaved = true;
            buttonSave.Enabled = false;
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

        private void ApplyPersonalRestDetail()
        {
            const string personalRest = "개인 휴식";
            const string restroom = "화장실이용";

            string level1 = comboBoxLevel1.SelectedItem?.ToString() ?? string.Empty;
            string level2 = comboBoxLevel2.SelectedItem?.ToString() ?? string.Empty;
            string level3 = labelLevel3Value.Text;

            bool isPersonalRest = level1 == "개인용무" && (level2 == "휴식" || level3 == "휴식");
            bool isRestroom = level3 == restroom;

            void ApplyAutoDetail(string marker, bool shouldApply)
            {
                if (shouldApply)
                {
                    if (string.IsNullOrWhiteSpace(textBoxDetail.Text) || textBoxDetail.Text == marker)
                    {
                        textBoxDetail.Text = marker;
                    }
                }
                else if (textBoxDetail.Text == marker)
                {
                    textBoxDetail.Clear();
                }
            }

            ApplyAutoDetail(personalRest, isPersonalRest);
            ApplyAutoDetail(restroom, isRestroom);
        }
    }
    public class AwayReason
    {
        public string ReasonCode { get; set; } = string.Empty;
        public string Level1 { get; set; } = string.Empty;
        public string Level2 { get; set; } = string.Empty;
        public string Level3 { get; set; } = string.Empty;
        public bool IsWorkApproved { get; set; }
    }
}
