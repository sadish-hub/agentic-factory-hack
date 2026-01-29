using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

/// <summary>
/// Data access service for Cosmos DB operations.
/// Handles technicians, parts inventory, and work orders.
/// </summary>
public sealed class CosmosDbService : IAsyncDisposable
{
    private readonly CosmosClient _client;
    private readonly Container _techniciansContainer;
    private readonly Container _partsContainer;
    private readonly Container _workOrdersContainer;
    private readonly ILogger<CosmosDbService> _logger;

    public CosmosDbService(CosmosDbOptions options, ILogger<CosmosDbService> logger)
    {
        _logger = logger;

        // Create Cosmos client with recommended settings
        _client = new CosmosClient(options.Endpoint, options.Key, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });

        var database = _client.GetDatabase(options.DatabaseName);

        // Get container references (containers must already exist)
        _techniciansContainer = database.GetContainer("Technicians");
        _partsContainer = database.GetContainer("PartsInventory");
        _workOrdersContainer = database.GetContainer("WorkOrders");

        _logger.LogInformation("CosmosDbService initialized for database: {Database}", options.DatabaseName);
    }

    /// <summary>
    /// Queries technicians who have at least one of the required skills and are available.
    /// </summary>
    /// <param name="requiredSkills">List of skills to match against</param>
    /// <returns>List of available technicians with matching skills</returns>
    public async Task<List<Technician>> GetAvailableTechniciansWithSkillsAsync(
        IReadOnlyList<string> requiredSkills,
        CancellationToken ct = default)
    {
        var technicians = new List<Technician>();

        if (requiredSkills.Count == 0)
        {
            _logger.LogWarning("No required skills provided, returning empty list");
            return technicians;
        }

        try
        {
            // Build query to find technicians with matching skills who are available
            // ARRAY_CONTAINS checks if the skill exists in the technician's skills array
            var skillConditions = string.Join(" OR ",
                requiredSkills.Select((_, i) => $"ARRAY_CONTAINS(t.skills, @skill{i})"));

            var queryText = $@"
                SELECT * FROM t 
                WHERE t.available = true 
                AND ({skillConditions})";

            var queryDef = new QueryDefinition(queryText);

            // Add parameters for each skill (prevents SQL injection)
            for (int i = 0; i < requiredSkills.Count; i++)
            {
                queryDef = queryDef.WithParameter($"@skill{i}", requiredSkills[i]);
            }

            _logger.LogDebug("Querying technicians with skills: {Skills}", string.Join(", ", requiredSkills));

            // Execute query and collect results
            using var iterator = _techniciansContainer.GetItemQueryIterator<Technician>(queryDef);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                technicians.AddRange(response);
            }

            _logger.LogInformation("Found {Count} available technicians with required skills", technicians.Count);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos DB error querying technicians: {Message}", ex.Message);
            throw;
        }

        return technicians;
    }

    /// <summary>
    /// Fetches parts from inventory by their part numbers.
    /// </summary>
    /// <param name="partNumbers">List of part numbers to fetch</param>
    /// <returns>List of parts found in inventory</returns>
    public async Task<List<Part>> GetPartsByNumbersAsync(
        IReadOnlyList<string> partNumbers,
        CancellationToken ct = default)
    {
        var parts = new List<Part>();

        if (partNumbers.Count == 0)
        {
            _logger.LogDebug("No part numbers provided, returning empty list");
            return parts;
        }

        try
        {
            // Build IN clause for part numbers
            var parameters = string.Join(", ", partNumbers.Select((_, i) => $"@part{i}"));
            var queryText = $"SELECT * FROM p WHERE p.partNumber IN ({parameters})";

            var queryDef = new QueryDefinition(queryText);

            for (int i = 0; i < partNumbers.Count; i++)
            {
                queryDef = queryDef.WithParameter($"@part{i}", partNumbers[i]);
            }

            _logger.LogDebug("Querying parts: {PartNumbers}", string.Join(", ", partNumbers));

            using var iterator = _partsContainer.GetItemQueryIterator<Part>(queryDef);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                parts.AddRange(response);
            }

            _logger.LogInformation("Found {Count}/{Requested} parts in inventory",
                parts.Count, partNumbers.Count);

            // Log any missing parts
            var foundNumbers = parts.Select(p => p.PartNumber).ToHashSet();
            var missing = partNumbers.Where(pn => !foundNumbers.Contains(pn)).ToList();
            if (missing.Count > 0)
            {
                _logger.LogWarning("Parts not found in inventory: {Missing}", string.Join(", ", missing));
            }
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos DB error querying parts: {Message}", ex.Message);
            throw;
        }

        return parts;
    }

    /// <summary>
    /// Creates a new work order in Cosmos DB.
    /// </summary>
    /// <param name="workOrder">The work order to create</param>
    /// <returns>The ID of the created work order</returns>
    public async Task<string> CreateWorkOrderAsync(WorkOrder workOrder, CancellationToken ct = default)
    {
        try
        {
            // Ensure ID is set (Cosmos requires an id field)
            if (string.IsNullOrEmpty(workOrder.Id))
            {
                workOrder.Id = Guid.NewGuid().ToString();
            }

            // Set timestamps
            workOrder.CreatedAt = DateTime.UtcNow;
            workOrder.UpdatedAt = DateTime.UtcNow;

            _logger.LogDebug("Creating work order: {WorkOrderNumber} for machine: {MachineId}",
                workOrder.WorkOrderNumber, workOrder.MachineId);

            // Partition key is "status" - must match container configuration
            var response = await _workOrdersContainer.CreateItemAsync(
                workOrder,
                new PartitionKey(workOrder.Status),
                cancellationToken: ct);

            _logger.LogInformation(
                "Work order created: {Id} (RU charge: {RU})",
                response.Resource.Id,
                response.RequestCharge);

            return response.Resource.Id;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos DB error creating work order: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Disposes the Cosmos client.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _logger.LogDebug("CosmosDbService disposed");
        return ValueTask.CompletedTask;
    }
}
