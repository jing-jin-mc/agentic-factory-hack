using System.Text.Json;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner;

/// <summary>
/// Orchestrates the repair planning workflow using the Foundry Agents SDK.
/// Takes diagnosed faults and generates comprehensive work orders with tasks and resource allocation.
/// </summary>
public sealed class RepairPlannerAgent(
    AIProjectClient projectClient,
    CosmosDbService cosmosDb,
    IFaultMappingService faultMapping,
    string modelDeploymentName,
    ILogger<RepairPlannerAgent> logger)
{
    private const string AgentName = "RepairPlannerAgent";
    
    // Agent instructions with JSON schema for structured output
    private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment.
        Your role is to generate comprehensive repair plans based on diagnosed faults.
        
        You will receive:
        - Diagnosed fault information (machine, fault type, severity, description)
        - Available technicians with their skills and experience
        - Required parts from inventory
        
        Generate a repair plan as valid JSON matching the WorkOrder schema.
        
        Output JSON with these fields:
        - workOrderNumber: string (format: WO-YYYYMMDD-XXXX)
        - machineId: string
        - title: string (concise summary)
        - description: string (detailed explanation)
        - type: "corrective" | "preventive" | "emergency"
        - priority: "critical" | "high" | "medium" | "low"
        - status: "pending" (default)
        - assignedTo: string (technician id) or null if unassigned
        - notes: string (additional context)
        - estimatedDuration: integer (total minutes, e.g. 120 not "120 minutes")
        - partsUsed: array of { partId, partNumber, quantity }
        - tasks: array of { sequence, title, description, estimatedDurationMinutes (integer), requiredSkills, safetyNotes }
        
        CRITICAL RULES:
        1. All duration fields MUST be integers representing minutes (e.g. 90), NOT strings like "90 minutes"
        2. Assign the most qualified available technician (highest skill match + experience)
        3. Include only relevant parts from the provided list; use empty array if none needed
        4. Tasks must be ordered by sequence number and actionable
        5. Sum of task durations should roughly equal estimatedDuration
        6. Include safety notes for hazardous operations
        7. Set priority based on severity: critical/high → critical/high priority, medium/low → medium/low priority
        8. Set type: emergency if critical priority, corrective for fault repairs, preventive for scheduled maintenance
        
        Return ONLY the JSON object, no markdown formatting or explanation.
        """;

    // JSON options for parsing LLM responses (allows "60" to be parsed as 60)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Registers the agent version with Azure AI Foundry.
    /// Call this once during startup or whenever agent instructions change.
    /// </summary>
    public async Task EnsureAgentVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Registering agent version for {AgentName}", AgentName);
            
            var definition = new PromptAgentDefinition(model: modelDeploymentName)
            {
                Instructions = AgentInstructions
            };

            await projectClient.Agents.CreateAgentVersionAsync(
                AgentName,
                new AgentVersionCreationOptions(definition),
                cancellationToken);

            logger.LogInformation("Agent version registered successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register agent version");
            throw;
        }
    }

    /// <summary>
    /// Main workflow: Takes a diagnosed fault and generates a complete work order.
    /// </summary>
    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(
        DiagnosedFault fault,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "Starting repair planning for machine {MachineId}, fault: {FaultType}",
                fault.MachineId, fault.FaultType);

            // Step 1: Get required skills and parts from mapping
            var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType);
            var requiredPartNumbers = faultMapping.GetRequiredParts(fault.FaultType);

            logger.LogInformation(
                "Required skills: {Skills}, Required parts: {Parts}",
                string.Join(", ", requiredSkills),
                string.Join(", ", requiredPartNumbers));

            // Step 2: Query available technicians and parts from Cosmos DB
            var techniciansTask = cosmosDb.GetAvailableTechniciansBySkillsAsync(
                requiredSkills, cancellationToken);
            var partsTask = cosmosDb.GetPartsByPartNumbersAsync(
                requiredPartNumbers, cancellationToken);

            await Task.WhenAll(techniciansTask, partsTask);

            var technicians = await techniciansTask;
            var parts = await partsTask;

            logger.LogInformation(
                "Found {TechnicianCount} available technicians and {PartCount} parts",
                technicians.Count, parts.Count);

            // Step 3: Build the prompt with all context
            var userPrompt = BuildPrompt(fault, requiredSkills, requiredPartNumbers, technicians, parts);

            logger.LogDebug("Agent prompt: {Prompt}", userPrompt);

            // Step 4: Invoke the Foundry Agent
            var agent = projectClient.GetAIAgent(name: AgentName);
            
            logger.LogInformation("Invoking agent to generate repair plan...");
            var response = await agent.RunAsync(userPrompt, thread: null, options: null, cancellationToken);
            
            string jsonResponse = response.Text ?? "";
            
            logger.LogDebug("Agent response: {Response}", jsonResponse);

            // Step 5: Parse the response into WorkOrder
            var workOrder = ParseWorkOrder(jsonResponse, fault);

            // Step 6: Apply defaults and validate
            ApplyDefaults(workOrder, fault, technicians);

            // Step 7: Save to Cosmos DB
            var savedWorkOrder = await cosmosDb.CreateWorkOrderAsync(workOrder, cancellationToken);

            logger.LogInformation(
                "Work order {WorkOrderNumber} created successfully for machine {MachineId}",
                savedWorkOrder.WorkOrderNumber, savedWorkOrder.MachineId);

            return savedWorkOrder;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to plan and create work order for fault {FaultType}", fault.FaultType);
            throw;
        }
    }

    /// <summary>
    /// Builds the prompt with all context for the agent
    /// </summary>
    private string BuildPrompt(
        DiagnosedFault fault,
        IReadOnlyList<string> requiredSkills,
        IReadOnlyList<string> requiredPartNumbers,
        List<Technician> technicians,
        List<Part> parts)
    {
        var techniciansSummary = technicians.Count > 0
            ? string.Join("\n", technicians.Select(t =>
                $"- {t.Name} (ID: {t.Id}, Skills: {string.Join(", ", t.Skills)}, Experience: {t.ExperienceYears} years)"))
            : "- No available technicians found";

        var partsSummary = parts.Count > 0
            ? string.Join("\n", parts.Select(p =>
                $"- {p.Name} (ID: {p.Id}, Part#: {p.PartNumber}, Available: {p.QuantityAvailable})"))
            : "- No parts required";

        return $"""
            DIAGNOSED FAULT:
            - Machine ID: {fault.MachineId}
            - Fault Type: {fault.FaultType}
            - Severity: {fault.Severity}
            - Description: {fault.Description}
            - Detected At: {fault.DetectedAt:yyyy-MM-dd HH:mm:ss} UTC
            - Estimated Downtime: {fault.EstimatedDowntimeMinutes} minutes
            - Recommended Actions: {string.Join("; ", fault.RecommendedActions)}
            
            REQUIRED SKILLS:
            {string.Join(", ", requiredSkills)}
            
            REQUIRED PARTS:
            {string.Join(", ", requiredPartNumbers)}
            
            AVAILABLE TECHNICIANS (sorted by qualification):
            {techniciansSummary}
            
            AVAILABLE PARTS:
            {partsSummary}
            
            Generate a comprehensive repair plan as a JSON work order.
            """;
    }

    /// <summary>
    /// Parses the agent's JSON response into a WorkOrder object
    /// </summary>
    private WorkOrder ParseWorkOrder(string jsonResponse, DiagnosedFault fault)
    {
        try
        {
            // Try to extract JSON if wrapped in markdown code blocks
            var json = jsonResponse.Trim();
            if (json.StartsWith("```json"))
            {
                json = json.Substring(7);
            }
            if (json.StartsWith("```"))
            {
                json = json.Substring(3);
            }
            if (json.EndsWith("```"))
            {
                json = json.Substring(0, json.Length - 3);
            }
            json = json.Trim();

            var workOrder = JsonSerializer.Deserialize<WorkOrder>(json, JsonOptions);
            
            if (workOrder == null)
            {
                throw new InvalidOperationException("Agent returned null work order");
            }

            return workOrder;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse agent response as JSON. Response: {Response}", jsonResponse);
            throw new InvalidOperationException("Agent returned invalid JSON", ex);
        }
    }

    /// <summary>
    /// Applies default values and ensures data consistency
    /// </summary>
    private void ApplyDefaults(WorkOrder workOrder, DiagnosedFault fault, List<Technician> technicians)
    {
        // ??= means "assign if null" (like Python: x = x or default)
        workOrder.Id ??= Guid.NewGuid().ToString();
        workOrder.Status ??= "pending";
        workOrder.Priority ??= "medium";
        workOrder.Type ??= "corrective";
        
        // Generate work order number if not provided
        if (string.IsNullOrEmpty(workOrder.WorkOrderNumber))
        {
            workOrder.WorkOrderNumber = $"WO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";
        }

        // Ensure machine ID matches
        if (string.IsNullOrEmpty(workOrder.MachineId))
        {
            workOrder.MachineId = fault.MachineId;
        }

        // Validate assigned technician exists
        if (!string.IsNullOrEmpty(workOrder.AssignedTo))
        {
            var technicianExists = technicians.Any(t => t.Id == workOrder.AssignedTo);
            if (!technicianExists)
            {
                logger.LogWarning(
                    "Assigned technician {TechnicianId} not found in available list, setting to null",
                    workOrder.AssignedTo);
                workOrder.AssignedTo = null;
            }
        }

        // Ensure tasks are ordered
        for (int i = 0; i < workOrder.Tasks.Count; i++)
        {
            if (workOrder.Tasks[i].Sequence == 0)
            {
                workOrder.Tasks[i].Sequence = i + 1;
            }
        }

        // Initialize empty collections if null
        workOrder.Tasks ??= new List<RepairTask>();
        workOrder.PartsUsed ??= new List<WorkOrderPartUsage>();
        workOrder.Notes ??= string.Empty;
    }
}
