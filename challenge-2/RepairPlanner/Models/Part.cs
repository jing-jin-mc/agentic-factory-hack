using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Represents a spare part in inventory
/// </summary>
public sealed class Part
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("partNumber")]
    [JsonProperty("partNumber")]
    public string PartNumber { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("quantityAvailable")]
    [JsonProperty("quantityAvailable")]
    public int QuantityAvailable { get; set; }

    [JsonPropertyName("reorderPoint")]
    [JsonProperty("reorderPoint")]
    public int ReorderPoint { get; set; }

    [JsonPropertyName("unitCost")]
    [JsonProperty("unitCost")]
    public decimal UnitCost { get; set; }

    [JsonPropertyName("location")]
    [JsonProperty("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("supplier")]
    [JsonProperty("supplier")]
    public string Supplier { get; set; } = string.Empty;

    [JsonPropertyName("leadTimeDays")]
    [JsonProperty("leadTimeDays")]
    public int LeadTimeDays { get; set; }
}
