using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// Optional background housekeeping for durable agent memory.
/// TTL/near-duplicate cleanup is enabled by default; semantic consolidation is opt-in.
/// </summary>
public sealed class MemoryBackgroundMaintenanceService : BackgroundService
{
    private readonly ModelMemoryService _memory;
    private readonly ILogger<MemoryBackgroundMaintenanceService> _logger;

    public MemoryBackgroundMaintenanceService(ModelMemoryService memory, ILogger<MemoryBackgroundMaintenanceService> logger)
    {
        _memory = memory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = ReadDouble("MEMORY_MAINTENANCE_INTERVAL_MINUTES", 60.0, 1.0, 10080.0);
        var initialDelaySeconds = ReadDouble("MEMORY_MAINTENANCE_INITIAL_DELAY_SECONDS", 30.0, 0.0, 3600.0);
        var autoConsolidate = ReadBool("MEMORY_AUTO_CONSOLIDATE", false);
        var duplicateThreshold = ReadDouble("MEMORY_MAINTENANCE_DUPLICATE_THRESHOLD", 0.995, 0.97, 0.99999);
        var maxScan = (int)ReadDouble("MEMORY_MAINTENANCE_MAX_SCAN", 500, 10, 5000);

        if (initialDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(initialDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var maintenance = await _memory.MaintainAsync(
                    memoryNamespace: null,
                    removeExpired: true,
                    removeNearDuplicates: true,
                    duplicateThreshold: duplicateThreshold,
                    maxScan: maxScan,
                    ct: stoppingToken);

                _logger.LogDebug("Memory maintenance completed: expired={Expired}, duplicates={Duplicates}",
                    maintenance.ExpiredRemoved, maintenance.NearDuplicatesRemoved);

                if (autoConsolidate)
                    await RunConsolidationCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background memory maintenance failed; the next cycle will retry.");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task RunConsolidationCycleAsync(CancellationToken ct)
    {
        var namespaces = (Environment.GetEnvironmentVariable("MEMORY_AUTO_CONSOLIDATE_NAMESPACES")
                          ?? "projects,tasks,knowledge,user,agents")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var threshold = ReadDouble("MEMORY_AUTO_CONSOLIDATE_SIMILARITY", 0.86, 0.50, 0.999);
        var minCluster = (int)ReadDouble("MEMORY_AUTO_CONSOLIDATE_MIN_CLUSTER", 4, 2, 20);
        var maxSources = (int)ReadDouble("MEMORY_AUTO_CONSOLIDATE_MAX_SOURCES", 8, minCluster, 30);
        var sourceFactor = ReadDouble("MEMORY_AUTO_CONSOLIDATE_SOURCE_IMPORTANCE_FACTOR", 0.60, 0.05, 1.0);

        foreach (var memoryNamespace in namespaces)
        {
            var result = await _memory.ConsolidateAsync(
                memoryNamespace: memoryNamespace,
                seedQuery: null,
                summaryContent: null,
                summaryKey: null,
                similarityThreshold: threshold,
                minClusterSize: minCluster,
                maxSources: maxSources,
                maxScan: 500,
                sourceImportanceFactor: sourceFactor,
                summaryImportanceBoost: 0.10,
                ct: ct);

            if (result.Action == "consolidated")
                _logger.LogInformation("Consolidated {Count} memories in namespace {Namespace} into {Key}",
                    result.Sources.Count, memoryNamespace, result.Summary?.Key);
        }
    }

    private static bool ReadBool(string name, bool fallback)
        => bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static double ReadDouble(string name, double fallback, double min, double max)
        => double.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, min, max)
            : fallback;
}
