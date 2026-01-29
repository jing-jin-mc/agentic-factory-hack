using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Input from the Fault Diagnosis Agent - represents a diagnosed equipment fault
/// </summary>
public sealed class DiagnosedFault
{
    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("faultType")]
    [JsonProperty("faultType")]
    public string FaultType { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    [JsonProperty("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("detectedAt")]
    [JsonProperty("detectedAt")]
    public DateTime DetectedAt { get; set; }

    [JsonPropertyName("recommendedActions")]
    [JsonProperty("recommendedActions")]
    public List<string> RecommendedActions { get; set; } = new();

    [JsonPropertyName("estimatedDowntimeMinutes")]
    [JsonProperty("estimatedDowntimeMinutes")]
    public int EstimatedDowntimeMinutes { get; set; }
}
