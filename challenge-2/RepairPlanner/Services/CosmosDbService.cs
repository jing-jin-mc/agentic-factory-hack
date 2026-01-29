using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

/// <summary>
/// Service for interacting with Azure Cosmos DB
/// </summary>
public sealed class CosmosDbService : IAsyncDisposable
{
    private readonly CosmosClient _cosmosClient;
    private readonly Database _database;
    private readonly Container _techniciansContainer;
    private readonly Container _partsInventoryContainer;
    private readonly Container _workOrdersContainer;
    private readonly ILogger<CosmosDbService> _logger;

    public CosmosDbService(CosmosDbOptions options, ILogger<CosmosDbService> logger)
    {
        _logger = logger;
        
        _cosmosClient = new CosmosClient(options.Endpoint, options.Key);
        _database = _cosmosClient.GetDatabase(options.DatabaseName);
        
        _techniciansContainer = _database.GetContainer(options.TechniciansContainer);
        _partsInventoryContainer = _database.GetContainer(options.PartsInventoryContainer);
        _workOrdersContainer = _database.GetContainer(options.WorkOrdersContainer);
        
        _logger.LogInformation("CosmosDbService initialized for database: {DatabaseName}", options.DatabaseName);
    }

    /// <summary>
    /// Query technicians who have at least one of the required skills and are available.
    /// Returns technicians sorted by number of matching skills (most qualified first).
    /// </summary>
    public async Task<List<Technician>> GetAvailableTechniciansBySkillsAsync(
        IEnumerable<string> requiredSkills,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var skillsList = requiredSkills.ToList();
            _logger.LogInformation("Querying technicians with skills: {Skills}", string.Join(", ", skillsList));

            // Query for available technicians (status = "available")
            // Cross-partition query since we need to search across all departments
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.currentStatus = @status"
            ).WithParameter("@status", "available");

            var technicians = new List<Technician>();
            
            using var iterator = _techniciansContainer.GetItemQueryIterator<Technician>(query);
            
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                technicians.AddRange(response);
            }

            // Filter and sort in memory: technicians with at least one matching skill
            // Sort by number of matching skills (descending), then by experience
            var matchedTechnicians = technicians
                .Where(t => t.Skills.Any(skill => 
                    skillsList.Contains(skill, StringComparer.OrdinalIgnoreCase)))
                .Select(t => new
                {
                    Technician = t,
                    MatchingSkillCount = t.Skills.Count(skill => 
                        skillsList.Contains(skill, StringComparer.OrdinalIgnoreCase))
                })
                .OrderByDescending(x => x.MatchingSkillCount)
                .ThenByDescending(x => x.Technician.ExperienceYears)
                .Select(x => x.Technician)
                .ToList();

            _logger.LogInformation(
                "Found {Count} available technicians with matching skills", 
                matchedTechnicians.Count);

            return matchedTechnicians;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, 
                "Cosmos DB error while querying technicians. Status: {Status}, Message: {Message}", 
                ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while querying technicians");
            throw;
        }
    }

    /// <summary>
    /// Fetch parts from inventory by part numbers
    /// </summary>
    public async Task<List<Part>> GetPartsByPartNumbersAsync(
        IEnumerable<string> partNumbers,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var partNumbersList = partNumbers.ToList();
            
            if (partNumbersList.Count == 0)
            {
                _logger.LogInformation("No parts requested, returning empty list");
                return new List<Part>();
            }

            _logger.LogInformation("Fetching parts: {PartNumbers}", string.Join(", ", partNumbersList));

            // Build parameterized query for part numbers
            // Cross-partition query since parts may be in different categories
            var parameterNames = partNumbersList
                .Select((_, index) => $"@partNumber{index}")
                .ToList();
            
            var inClause = string.Join(", ", parameterNames);
            var queryText = $"SELECT * FROM c WHERE c.partNumber IN ({inClause})";
            
            var query = new QueryDefinition(queryText);
            for (int i = 0; i < partNumbersList.Count; i++)
            {
                query = query.WithParameter(parameterNames[i], partNumbersList[i]);
            }

            var parts = new List<Part>();
            
            using var iterator = _partsInventoryContainer.GetItemQueryIterator<Part>(query);
            
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                parts.AddRange(response);
            }

            _logger.LogInformation("Found {Count} parts out of {Requested} requested", 
                parts.Count, partNumbersList.Count);

            // Log any missing parts
            var foundPartNumbers = parts.Select(p => p.PartNumber).ToHashSet();
            var missingParts = partNumbersList.Where(pn => !foundPartNumbers.Contains(pn)).ToList();
            if (missingParts.Count > 0)
            {
                _logger.LogWarning("Parts not found in inventory: {MissingParts}", 
                    string.Join(", ", missingParts));
            }

            return parts;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, 
                "Cosmos DB error while querying parts. Status: {Status}, Message: {Message}", 
                ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while querying parts");
            throw;
        }
    }

    /// <summary>
    /// Create a new work order in Cosmos DB
    /// </summary>
    public async Task<WorkOrder> CreateWorkOrderAsync(
        WorkOrder workOrder,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate ID if not provided
            if (string.IsNullOrEmpty(workOrder.Id))
            {
                workOrder.Id = Guid.NewGuid().ToString();
            }

            // Set created timestamp
            workOrder.CreatedAt = DateTime.UtcNow;

            // Ensure status is set (it's the partition key)
            workOrder.Status ??= "pending";

            _logger.LogInformation(
                "Creating work order {WorkOrderNumber} for machine {MachineId}", 
                workOrder.WorkOrderNumber, workOrder.MachineId);

            // Use status as partition key
            var response = await _workOrdersContainer.CreateItemAsync(
                workOrder,
                new PartitionKey(workOrder.Status),
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Work order created successfully. ID: {Id}, RU charge: {RU}", 
                response.Resource.Id, response.RequestCharge);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogError(ex, 
                "Work order with ID {Id} already exists", workOrder.Id);
            throw new InvalidOperationException(
                $"Work order with ID {workOrder.Id} already exists", ex);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, 
                "Cosmos DB error while creating work order. Status: {Status}, Message: {Message}", 
                ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating work order");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cosmosClient?.Dispose();
        await Task.CompletedTask;
    }
}
