using System.Text.Json.Serialization;

namespace YEJI_AW_Client
{
    public class ClientStatusRequest
    {
        [JsonPropertyName("empNo")]
        public string EmpNo { get; set; } = string.Empty;

        [JsonPropertyName("empName")]
        public string EmpName { get; set; } = string.Empty;

        [JsonPropertyName("pcName")]
        public string PcName { get; set; } = string.Empty;

        [JsonPropertyName("installed")]
        public int Installed { get; set; }
            = 1;

        [JsonPropertyName("clientVersion")]
        public string ClientVersion { get; set; } = string.Empty;

        [JsonPropertyName("ip")]
        public string Ip { get; set; } = string.Empty;
    }
}