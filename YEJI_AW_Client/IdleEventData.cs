using System.Text.Json.Serialization;

namespace YEJI_AW_Client
{
    public class IdleEventData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
        [JsonPropertyName("employeeId")]
        public string EmployeeId { get; set; } = "";
        [JsonPropertyName("employeeName")]
        public string EmployeeName { get; set; } = "";
        [JsonPropertyName("computerName")]
        public string ComputerName { get; set; } = "";
        [JsonPropertyName("computerIp")]
        public string ComputerIP { get; set; } = "";
        [JsonPropertyName("idleStartTime")]
        public string IdleStartTime { get; set; } = "";
        [JsonPropertyName("idleEndTime")]
        public string IdleEndTime { get; set; } = "";
        [JsonPropertyName("reasonCategory")]
        public string ReasonCategory { get; set; } = "";
        [JsonPropertyName("reasonDetail")]
        public string ReasonDetail { get; set; } = "";
    }
}
