using System.Text.Json.Serialization;

namespace AI.Caller.Core.Models {
    public class IntentResult {
        [JsonPropertyName("label")]
        public string Label { get; set; } = "unknown";

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; } = 0.0;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } // 调试用，让AI解释为什么
    }
}