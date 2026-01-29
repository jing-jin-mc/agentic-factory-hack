using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Represents a maintenance technician with skills and availability
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

    [JsonPropertyName("experienceYears")]
    [JsonProperty("experienceYears")]
    public int ExperienceYears { get; set; }

    [JsonPropertyName("currentStatus")]
    [JsonProperty("currentStatus")]
    public string CurrentStatus { get; set; } = string.Empty;

    [JsonPropertyName("shift")]
    [JsonProperty("shift")]
    public string Shift { get; set; } = string.Empty;

    [JsonPropertyName("contactInfo")]
    [JsonProperty("contactInfo")]
    public TechnicianContactInfo ContactInfo { get; set; } = new();
}

/// <summary>
/// Contact information for a technician
/// </summary>
public sealed class TechnicianContactInfo
{
    [JsonPropertyName("email")]
    [JsonProperty("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    [JsonProperty("phone")]
    public string Phone { get; set; } = string.Empty;
}
