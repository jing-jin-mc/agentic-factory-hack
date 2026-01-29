namespace RepairPlanner.Services;

/// <summary>
/// Configuration options for Cosmos DB connection
/// </summary>
public sealed class CosmosDbOptions
{
    public required string Endpoint { get; set; }
    public required string Key { get; set; }
    public required string DatabaseName { get; set; }
    
    // Container names
    public string TechniciansContainer { get; set; } = "Technicians";
    public string PartsInventoryContainer { get; set; } = "PartsInventory";
    public string WorkOrdersContainer { get; set; } = "WorkOrders";
}
