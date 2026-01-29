using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner;

/// <summary>
/// Orchestrates the repair planning workflow using the Foundry Agents SDK.
/// </summary>
public sealed class RepairPlannerAgent
{
    private readonly AIProjectClient _projectClient;
    private readonly CosmosDbService _cosmosDb;
    private readonly IFaultMappingService _faultMapping;
    private readonly string _modelDeploymentName;
    private readonly ILogger<RepairPlannerAgent> _logger;

    private const string AgentName = "RepairPlannerAgent";

    // System prompt for the LLM - defines how it should generate repair plans
    private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment.
        Your job is to generate comprehensive repair plans based on diagnosed faults.

        Given information about:
        - The diagnosed fault (type, severity, machine affected)
        - Available technicians (with their skills)
        - Available parts (from inventory)

        Generate a repair plan as a JSON object with these fields:
        - workOrderNumber: string (format: "WO-YYYYMMDD-XXXX" where XXXX is random)
        - machineId: string (from the fault)
        - title: string (brief description of the repair)
        - description: string (detailed description)
        - type: "corrective" | "preventive" | "emergency"
        - priority: "critical" | "high" | "medium" | "low"
        - status: "pending"
        - assignedTo: string (technician id) or null if no suitable technician
        - estimatedDuration: integer (total minutes, e.g., 90 not "90 minutes")
        - notes: string (additional context)
        - partsUsed: array of { partId, partNumber, quantity }
        - tasks: array of repair steps, each with:
          - sequence: integer (1, 2, 3, ...)
          - title: string
          - description: string
          - estimatedDurationMinutes: integer (e.g., 30 not "30 minutes")
          - requiredSkills: array of strings
          - safetyNotes: string or null

        IMPORTANT RULES:
        1. All duration fields MUST be integers representing minutes (e.g., 90), not strings
        2. Assign the most qualified available technician based on skill match
        3. Only include parts that are available in the provided inventory
        4. Tasks must be ordered logically and include safety notes where applicable
        5. Priority should match severity: critical fault = critical priority
        6. Return ONLY valid JSON, no markdown code blocks or extra text
        """;

    // JSON options for parsing LLM responses - handles numbers as strings
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Primary constructor - parameters become readonly fields automatically
    // Like Python's __init__ with self.param = param for each parameter
    public RepairPlannerAgent(
        AIProjectClient projectClient,
        CosmosDbService cosmosDb,
        IFaultMappingService faultMapping,
        string modelDeploymentName,
        ILogger<RepairPlannerAgent> logger)
    {
        _projectClient = projectClient;
        _cosmosDb = cosmosDb;
        _faultMapping = faultMapping;
        _modelDeploymentName = modelDeploymentName;
        _logger = logger;
    }

    /// <summary>
    /// Registers or updates the agent definition in Azure AI Projects.
    /// Call this once at startup to ensure the agent exists.
    /// </summary>
    public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Registering agent: {AgentName} with model: {Model}",
            AgentName, _modelDeploymentName);

        var definition = new PromptAgentDefinition(model: _modelDeploymentName)
        {
            Instructions = AgentInstructions
        };

        await _projectClient.Agents.CreateAgentVersionAsync(
            AgentName,
            new AgentVersionCreationOptions(definition),
            ct);

        _logger.LogInformation("Agent {AgentName} registered successfully", AgentName);
    }

    /// <summary>
    /// Main workflow: takes a diagnosed fault and creates a work order.
    /// </summary>
    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(
        DiagnosedFault fault,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Planning repair for fault: {FaultType} on machine: {MachineId} (severity: {Severity})",
            fault.FaultType, fault.MachineId, fault.Severity);

        // Step 1: Get required skills and parts from mapping service
        var requiredSkills = _faultMapping.GetRequiredSkills(fault.FaultType);
        var requiredPartNumbers = _faultMapping.GetRequiredParts(fault.FaultType);

        _logger.LogDebug("Required skills: {Skills}", string.Join(", ", requiredSkills));
        _logger.LogDebug("Required parts: {Parts}", string.Join(", ", requiredPartNumbers));

        // Step 2: Query Cosmos DB for available resources (in parallel for efficiency)
        var techniciansTask = _cosmosDb.GetAvailableTechniciansWithSkillsAsync(requiredSkills, ct);
        var partsTask = _cosmosDb.GetPartsByNumbersAsync(requiredPartNumbers, ct);

        // await both tasks - like Python's asyncio.gather()
        await Task.WhenAll(techniciansTask, partsTask);

        var technicians = await techniciansTask;
        var parts = await partsTask;

        _logger.LogInformation(
            "Found {TechCount} technicians and {PartCount} parts",
            technicians.Count, parts.Count);

        // Step 3: Build prompt with all context for the LLM
        var prompt = BuildPrompt(fault, technicians, parts);

        // Step 4: Invoke the LLM agent
        var workOrderJson = await InvokeAgentAsync(prompt, ct);

        // Step 5: Parse response and apply defaults
        var workOrder = ParseWorkOrder(workOrderJson, fault);

        // Step 6: Save to Cosmos DB
        var id = await _cosmosDb.CreateWorkOrderAsync(workOrder, ct);
        workOrder.Id = id;

        _logger.LogInformation(
            "Work order created: {WorkOrderNumber} (ID: {Id})",
            workOrder.WorkOrderNumber, id);

        return workOrder;
    }

    /// <summary>
    /// Builds the user prompt with all context for the LLM.
    /// </summary>
    private string BuildPrompt(DiagnosedFault fault, List<Technician> technicians, List<Part> parts)
    {
        // Serialize data to JSON for the prompt
        var faultJson = JsonSerializer.Serialize(fault, JsonOptions);
        var techniciansJson = JsonSerializer.Serialize(technicians, JsonOptions);
        var partsJson = JsonSerializer.Serialize(parts, JsonOptions);

        return $"""
            Create a repair plan for the following diagnosed fault:

            ## Diagnosed Fault
            {faultJson}

            ## Available Technicians
            {techniciansJson}

            ## Available Parts in Inventory
            {partsJson}

            Generate a complete work order as JSON. Remember:
            - Assign the best-matched available technician
            - Include only parts that are in the inventory above
            - Create logical, sequential repair tasks
            - All duration values must be integers (minutes)
            """;
    }

    /// <summary>
    /// Invokes the registered agent and returns the response text.
    /// </summary>
    private async Task<string> InvokeAgentAsync(string prompt, CancellationToken ct)
    {
        _logger.LogDebug("Invoking agent with prompt length: {Length} chars", prompt.Length);

        // Get the registered agent by name
        var agent = _projectClient.GetAIAgent(name: AgentName);

        // Run the agent with our prompt
        // thread: null = new conversation, options: null = use defaults
        var response = await agent.RunAsync(prompt, thread: null, options: null, ct);

        var result = response.Text ?? string.Empty;

        _logger.LogDebug("Agent response length: {Length} chars", result.Length);

        return result;
    }

    /// <summary>
    /// Parses the LLM response into a WorkOrder, applying defaults for missing fields.
    /// </summary>
    private WorkOrder ParseWorkOrder(string json, DiagnosedFault fault)
    {
        // Clean up response - remove markdown code blocks if present
        var cleanJson = json
            .Replace("```json", "")
            .Replace("```", "")
            .Trim();

        WorkOrder? workOrder;

        try
        {
            workOrder = JsonSerializer.Deserialize<WorkOrder>(cleanJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse work order JSON: {Json}", cleanJson);
            throw new InvalidOperationException("Failed to parse LLM response as WorkOrder", ex);
        }

        if (workOrder is null)
        {
            throw new InvalidOperationException("LLM returned null or empty work order");
        }

        // Apply defaults for any missing fields
        // ??= means "assign if null" (like Python's: x = x or default)
        workOrder.MachineId ??= fault.MachineId;
        workOrder.Status ??= "pending";
        workOrder.Priority ??= MapSeverityToPriority(fault.Severity);
        workOrder.Type ??= "corrective";
        workOrder.Tasks ??= new List<RepairTask>();
        workOrder.PartsUsed ??= new List<WorkOrderPartUsage>();

        // Generate work order number if not provided
        if (string.IsNullOrEmpty(workOrder.WorkOrderNumber))
        {
            workOrder.WorkOrderNumber = $"WO-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        }

        _logger.LogDebug(
            "Parsed work order: {Number} with {TaskCount} tasks and {PartCount} parts",
            workOrder.WorkOrderNumber, workOrder.Tasks.Count, workOrder.PartsUsed.Count);

        return workOrder;
    }

    /// <summary>
    /// Maps fault severity to work order priority.
    /// </summary>
    private static string MapSeverityToPriority(string severity)
    {
        // Pattern matching on strings - like Python's match/case
        return severity.ToLowerInvariant() switch
        {
            "critical" => "critical",
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => "medium" // default
        };
    }
}
