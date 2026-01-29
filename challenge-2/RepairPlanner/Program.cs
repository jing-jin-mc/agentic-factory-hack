using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;

// Load environment variables
var projectEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT environment variable is required");

var modelDeploymentName = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME")
    ?? throw new InvalidOperationException("MODEL_DEPLOYMENT_NAME environment variable is required");

var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
    ?? throw new InvalidOperationException("COSMOS_ENDPOINT environment variable is required");

var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY")
    ?? throw new InvalidOperationException("COSMOS_KEY environment variable is required");

var cosmosDatabase = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME")
    ?? throw new InvalidOperationException("COSMOS_DATABASE_NAME environment variable is required");

Console.WriteLine("=== Repair Planner Agent Demo ===\n");
Console.WriteLine($"Azure AI Project: {projectEndpoint}");
Console.WriteLine($"Model: {modelDeploymentName}");
Console.WriteLine($"Cosmos DB: {cosmosDatabase}\n");

// Configure services with dependency injection
var services = new ServiceCollection();

// Add logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Add Cosmos DB configuration
services.AddSingleton(new CosmosDbOptions
{
    Endpoint = cosmosEndpoint,
    Key = cosmosKey,
    DatabaseName = cosmosDatabase
});

// Add services
services.AddSingleton<CosmosDbService>();
services.AddSingleton<IFaultMappingService, FaultMappingService>();

// Add AIProjectClient (uses DefaultAzureCredential for authentication)
services.AddSingleton(sp => new AIProjectClient(
    new Uri(projectEndpoint),
    new DefaultAzureCredential()));

// Add RepairPlannerAgent
services.AddSingleton(sp => new RepairPlannerAgent(
    sp.GetRequiredService<AIProjectClient>(),
    sp.GetRequiredService<CosmosDbService>(),
    sp.GetRequiredService<IFaultMappingService>(),
    modelDeploymentName,
    sp.GetRequiredService<ILogger<RepairPlannerAgent>>()));

// Build service provider - await using ensures proper disposal (like Python's "async with")
await using var provider = services.BuildServiceProvider();

try
{
    // Get the agent
    var agent = provider.GetRequiredService<RepairPlannerAgent>();
    
    // Step 1: Register the agent version with Azure AI Foundry
    Console.WriteLine("Registering agent with Azure AI Foundry...");
    await agent.EnsureAgentVersionAsync();
    Console.WriteLine("✓ Agent registered successfully\n");

    // Step 2: Create a sample diagnosed fault
    Console.WriteLine("=== Sample Diagnosed Fault ===");
    var sampleFault = new DiagnosedFault
    {
        MachineId = "TCP-001",
        FaultType = "curing_temperature_excessive",
        Severity = "high",
        Description = "Curing press temperature sensor reading 15°C above setpoint. " +
                     "Heater elements may be malfunctioning or temperature controller calibration drift detected.",
        DetectedAt = DateTime.UtcNow.AddMinutes(-5),
        RecommendedActions = new List<string>
        {
            "Check heater element resistance",
            "Calibrate temperature sensors",
            "Inspect PLC temperature control logic",
            "Verify thermocouple connections"
        },
        EstimatedDowntimeMinutes = 120
    };

    Console.WriteLine($"Machine: {sampleFault.MachineId}");
    Console.WriteLine($"Fault: {sampleFault.FaultType}");
    Console.WriteLine($"Severity: {sampleFault.Severity}");
    Console.WriteLine($"Description: {sampleFault.Description}");
    Console.WriteLine($"Estimated Downtime: {sampleFault.EstimatedDowntimeMinutes} minutes\n");

    // Step 3: Generate repair plan and create work order
    Console.WriteLine("=== Generating Repair Plan ===");
    Console.WriteLine("Analyzing fault, querying resources, and invoking AI agent...\n");
    
    var workOrder = await agent.PlanAndCreateWorkOrderAsync(sampleFault);

    // Step 4: Display results
    Console.WriteLine("\n=== Work Order Created ===");
    Console.WriteLine($"Work Order #: {workOrder.WorkOrderNumber}");
    Console.WriteLine($"Machine: {workOrder.MachineId}");
    Console.WriteLine($"Title: {workOrder.Title}");
    Console.WriteLine($"Type: {workOrder.Type}");
    Console.WriteLine($"Priority: {workOrder.Priority}");
    Console.WriteLine($"Status: {workOrder.Status}");
    Console.WriteLine($"Assigned To: {workOrder.AssignedTo ?? "Unassigned"}");
    Console.WriteLine($"Estimated Duration: {workOrder.EstimatedDuration} minutes");
    Console.WriteLine($"Created: {workOrder.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
    
    Console.WriteLine($"\nDescription: {workOrder.Description}");
    
    if (workOrder.Tasks.Count > 0)
    {
        Console.WriteLine($"\n=== Repair Tasks ({workOrder.Tasks.Count}) ===");
        foreach (var task in workOrder.Tasks.OrderBy(t => t.Sequence))
        {
            Console.WriteLine($"\n{task.Sequence}. {task.Title}");
            Console.WriteLine($"   Description: {task.Description}");
            Console.WriteLine($"   Duration: {task.EstimatedDurationMinutes} minutes");
            Console.WriteLine($"   Skills: {string.Join(", ", task.RequiredSkills)}");
            if (!string.IsNullOrEmpty(task.SafetyNotes))
            {
                Console.WriteLine($"   ⚠️  Safety: {task.SafetyNotes}");
            }
        }
    }

    if (workOrder.PartsUsed.Count > 0)
    {
        Console.WriteLine($"\n=== Parts Required ({workOrder.PartsUsed.Count}) ===");
        foreach (var part in workOrder.PartsUsed)
        {
            Console.WriteLine($"- {part.PartNumber} (Qty: {part.Quantity})");
        }
    }
    else
    {
        Console.WriteLine("\n=== Parts Required ===");
        Console.WriteLine("No parts required for this repair");
    }

    if (!string.IsNullOrEmpty(workOrder.Notes))
    {
        Console.WriteLine($"\nNotes: {workOrder.Notes}");
    }

    Console.WriteLine("\n✓ Work order saved to Cosmos DB successfully!");
    Console.WriteLine($"\nWork Order ID: {workOrder.Id}");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    Console.WriteLine($"\nDetails: {ex}");
    return 1;
}

Console.WriteLine("\n=== Demo Complete ===");
return 0;
