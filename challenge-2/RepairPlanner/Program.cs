using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;

// ============================================================================
// Repair Planner Agent - Entry Point
// ============================================================================
// This console app demonstrates the Foundry Agents SDK pattern:
// 1. Read configuration from environment variables
// 2. Initialize services (Cosmos DB, Fault Mapping)
// 3. Register a Prompt Agent with Azure AI Projects
// 4. Process a diagnosed fault and generate a work order
// ============================================================================

Console.WriteLine("🔧 Repair Planner Agent - Starting...\n");

// ----------------------------------------------------------------------------
// Step 1: Read environment variables
// ----------------------------------------------------------------------------
// Environment.GetEnvironmentVariable returns null if not set
// ?? throws if null (like Python's: os.environ["VAR"] which raises KeyError)

var aiProjectEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT environment variable is required");

var modelDeploymentName = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME")
    ?? throw new InvalidOperationException("MODEL_DEPLOYMENT_NAME environment variable is required");

var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
    ?? throw new InvalidOperationException("COSMOS_ENDPOINT environment variable is required");

var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY")
    ?? throw new InvalidOperationException("COSMOS_KEY environment variable is required");

var cosmosDatabaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME")
    ?? throw new InvalidOperationException("COSMOS_DATABASE_NAME environment variable is required");

Console.WriteLine("✅ Environment variables loaded");
Console.WriteLine($"   AI Project: {aiProjectEndpoint}");
Console.WriteLine($"   Model: {modelDeploymentName}");
Console.WriteLine($"   Cosmos DB: {cosmosDatabaseName}\n");

// ----------------------------------------------------------------------------
// Step 2: Set up dependency injection and logging
// ----------------------------------------------------------------------------
// ServiceCollection is like Python's dependency injection container
// AddLogging configures console output with colors and timestamps

var services = new ServiceCollection();

services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Build the service provider - this creates all the configured services
// await using = async context manager (like Python's "async with")
await using var serviceProvider = services.BuildServiceProvider();

var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

// ----------------------------------------------------------------------------
// Step 3: Initialize services
// ----------------------------------------------------------------------------

// Cosmos DB configuration
var cosmosOptions = new CosmosDbOptions
{
    Endpoint = cosmosEndpoint,
    Key = cosmosKey,
    DatabaseName = cosmosDatabaseName
};

// Create Cosmos DB service (handles technicians, parts, work orders)
var cosmosLogger = loggerFactory.CreateLogger<CosmosDbService>();
await using var cosmosDb = new CosmosDbService(cosmosOptions, cosmosLogger);

Console.WriteLine("✅ Cosmos DB service initialized\n");

// Create fault mapping service (hardcoded skill/parts mappings)
IFaultMappingService faultMapping = new FaultMappingService();

// Create AI Project client using DefaultAzureCredential
// This automatically uses: Azure CLI, Managed Identity, VS Code, etc.
var projectClient = new AIProjectClient(
    new Uri(aiProjectEndpoint),
    new DefaultAzureCredential());

Console.WriteLine("✅ AI Project client initialized\n");

// Create the main Repair Planner Agent
var agentLogger = loggerFactory.CreateLogger<RepairPlannerAgent>();
var repairPlannerAgent = new RepairPlannerAgent(
    projectClient,
    cosmosDb,
    faultMapping,
    modelDeploymentName,
    agentLogger);

// ----------------------------------------------------------------------------
// Step 4: Register the agent with Azure AI Projects
// ----------------------------------------------------------------------------

Console.WriteLine("📝 Registering agent with Azure AI Projects...");
await repairPlannerAgent.EnsureAgentVersionAsync();
Console.WriteLine("✅ Agent registered\n");

// ----------------------------------------------------------------------------
// Step 5: Create a sample diagnosed fault (simulating input from Challenge 1)
// ----------------------------------------------------------------------------

var sampleFault = new DiagnosedFault
{
    Id = Guid.NewGuid().ToString(),
    MachineId = "TCP-001",
    MachineName = "Tire Curing Press #1",
    FaultType = "curing_temperature_excessive",
    Severity = "high",
    Description = "Temperature sensor readings show the curing press is operating 15°C above the optimal range. " +
                  "This could lead to over-cured tires with degraded rubber properties. " +
                  "Immediate attention required to prevent quality issues.",
    DiagnosedAt = DateTime.UtcNow,
    TelemetrySnapshot = new Dictionary<string, double>
    {
        ["temperature_zone1"] = 185.5,
        ["temperature_zone2"] = 188.2,
        ["temperature_zone3"] = 182.1,
        ["pressure_psi"] = 145.0,
        ["cycle_time_seconds"] = 720
    }
};

Console.WriteLine("🔍 Sample Diagnosed Fault:");
Console.WriteLine($"   Machine: {sampleFault.MachineName} ({sampleFault.MachineId})");
Console.WriteLine($"   Fault Type: {sampleFault.FaultType}");
Console.WriteLine($"   Severity: {sampleFault.Severity}");
Console.WriteLine($"   Description: {sampleFault.Description[..Math.Min(80, sampleFault.Description.Length)]}...\n");

// ----------------------------------------------------------------------------
// Step 6: Run the repair planning workflow
// ----------------------------------------------------------------------------

Console.WriteLine("🤖 Generating repair plan...\n");

try
{
    var workOrder = await repairPlannerAgent.PlanAndCreateWorkOrderAsync(sampleFault);

    // Pretty-print the result
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine("✅ WORK ORDER CREATED SUCCESSFULLY");
    Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

    Console.WriteLine($"Work Order #: {workOrder.WorkOrderNumber}");
    Console.WriteLine($"ID: {workOrder.Id}");
    Console.WriteLine($"Title: {workOrder.Title}");
    Console.WriteLine($"Type: {workOrder.Type}");
    Console.WriteLine($"Priority: {workOrder.Priority}");
    Console.WriteLine($"Status: {workOrder.Status}");
    Console.WriteLine($"Assigned To: {workOrder.AssignedTo ?? "(unassigned)"}");
    Console.WriteLine($"Estimated Duration: {workOrder.EstimatedDuration} minutes");
    Console.WriteLine();

    Console.WriteLine($"📋 Tasks ({workOrder.Tasks.Count}):");
    foreach (var task in workOrder.Tasks.OrderBy(t => t.Sequence))
    {
        Console.WriteLine($"   {task.Sequence}. {task.Title} ({task.EstimatedDurationMinutes} min)");
        if (!string.IsNullOrEmpty(task.SafetyNotes))
        {
            Console.WriteLine($"      ⚠️  {task.SafetyNotes}");
        }
    }
    Console.WriteLine();

    Console.WriteLine($"🔩 Parts Required ({workOrder.PartsUsed.Count}):");
    foreach (var part in workOrder.PartsUsed)
    {
        Console.WriteLine($"   - {part.PartNumber} (qty: {part.Quantity})");
    }
    Console.WriteLine();

    if (!string.IsNullOrEmpty(workOrder.Notes))
    {
        Console.WriteLine($"📝 Notes: {workOrder.Notes}");
        Console.WriteLine();
    }

    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine("Full JSON output:");
    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine(JsonSerializer.Serialize(workOrder, jsonOptions));
}
catch (Exception ex)
{
    Console.WriteLine("❌ Error generating repair plan:");
    Console.WriteLine($"   {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    }
    Environment.Exit(1);
}

Console.WriteLine("\n🎉 Repair Planner Agent completed successfully!");
