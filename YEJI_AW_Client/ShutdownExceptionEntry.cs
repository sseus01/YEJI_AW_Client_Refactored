using System.Text.Json.Serialization;

namespace YEJI_AW_Client
{
    public class ShutdownExceptionEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("employee_id")]
        public string EmployeeId { get; set; } = string.Empty;

        [JsonPropertyName("work_date")]
        public string WorkDate { get; set; } = string.Empty;

        [JsonPropertyName("from_time")]
        public string FromTime { get; set; } = string.Empty;

        [JsonPropertyName("to_time")]
        public string ToTime { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
            = string.Empty;

        [JsonPropertyName("created_by")]
        public string CreatedBy { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;
    }
}