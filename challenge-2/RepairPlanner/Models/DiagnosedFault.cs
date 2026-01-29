using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Input from the Fault Diagnosis Agent - represents a diagnosed fault on a machine.
/// </summary>
public sealed class DiagnosedFault
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    [JsonProperty("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("faultType")]
    [JsonProperty("faultType")]
    public string FaultType { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    [JsonProperty("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("diagnosedAt")]
    [JsonProperty("diagnosedAt")]
    public DateTime DiagnosedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("telemetrySnapshot")]
    [JsonProperty("telemetrySnapshot")]
    public Dictionary<string, double>? TelemetrySnapshot { get; set; }
}
