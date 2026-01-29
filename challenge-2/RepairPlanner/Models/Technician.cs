using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Represents a maintenance technician with skills and availability.
/// </summary>
public sealed class Technician
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    [JsonProperty("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("skills")]
    [JsonProperty("skills")]
    public List<string> Skills { get; set; } = new();

    [JsonPropertyName("certifications")]
    [JsonProperty("certifications")]
    public List<string> Certifications { get; set; } = new();

    [JsonPropertyName("shift")]
    [JsonProperty("shift")]
    public string Shift { get; set; } = string.Empty;

    [JsonPropertyName("available")]
    [JsonProperty("available")]
    public bool Available { get; set; } = true;

    [JsonPropertyName("currentWorkload")]
    [JsonProperty("currentWorkload")]
    public int CurrentWorkload { get; set; } = 0;

    [JsonPropertyName("maxWorkload")]
    [JsonProperty("maxWorkload")]
    public int MaxWorkload { get; set; } = 5;
}
