// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Aspire.Dashboard.Otlp.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aspire.Dashboard.Otlp.Storage;

/// <summary>
/// SQL Server implementation of telemetry persistence using Entity Framework Core.
/// </summary>
/// <remarks>
/// <para>
/// This implementation provides <strong>write-only persistence</strong> for Aspire Dashboard telemetry data.
/// Logs, traces, and metrics are written to SQL Server for long-term storage and compliance purposes,
/// but read operations (Get* methods) are not fully implemented and return empty/null results.
/// </para>
/// <para>
/// <strong>Design rationale:</strong>
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// The TelemetryRepository maintains an in-memory cache of recent telemetry data for real-time
/// Dashboard display. This cache is fast and optimized for the Dashboard's query patterns.
/// </description>
/// </item>
/// <item>
/// <description>
/// The TelemetryRepository.GetLogs, GetTraces, and similar methods implement a fallback pattern:
/// they first attempt to retrieve data from persistence, and if that returns empty results,
/// they automatically fall back to the in-memory cache.
/// </description>
/// </item>
/// <item>
/// <description>
/// Reconstructing OtlpLogEntry, OtlpTrace, and OtlpSpan from database entities requires access
/// to runtime dependencies (OtlpContext, OtlpResourceView, OtlpScope) that are not available
/// in the persistence layer, making full reconstruction complex and potentially error-prone.
/// </description>
/// </item>
/// <item>
/// <description>
/// For long-term telemetry analysis and querying, it's recommended to export data to dedicated
/// observability backends (Application Insights, Jaeger, Zipkin, etc.) rather than querying
/// the Dashboard's SQL Server directly.
/// </description>
/// </item>
/// </list>
/// <para>
/// <strong>What this implementation provides:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>✅ Persistent storage of logs, traces, spans, and metrics to SQL Server</description></item>
/// <item><description>✅ Data retention beyond the in-memory cache limits</description></item>
/// <item><description>✅ Compliance and audit trail capabilities</description></item>
/// <item><description>✅ Bulk clear operations by resource</description></item>
/// <item><description>⚠️ Read operations return empty results (Dashboard uses in-memory cache)</description></item>
/// </list>
/// <para>
/// To enable full database reads, you would need to implement reconstruction logic that:
/// serializes and stores OtlpContext configuration, rebuilds OtlpResource and OtlpResourceView
/// from stored data, reconstructs OtlpScope objects, and deserializes all attributes and relationships.
/// </para>
/// </remarks>
public sealed class SqlServerTelemetryPersistence : ITelemetryPersistence, IAsyncDisposable
{
    private readonly ILogger<SqlServerTelemetryPersistence> _logger;
    private readonly SqlServerTelemetryOptions _options;
    private readonly IDbContextFactory<SqlServerTelemetryDbContext> _contextFactory;
    private readonly OtlpContext _otlpContext;
    private readonly Dictionary<ResourceKey, OtlpResource> _resourceCache = new();
    private readonly Dictionary<string, OtlpScope> _scopeCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerTelemetryPersistence"/> class.
    /// </summary>
    public SqlServerTelemetryPersistence(
        IOptions<SqlServerTelemetryOptions> options,
        IDbContextFactory<SqlServerTelemetryDbContext> contextFactory,
        ILogger<SqlServerTelemetryPersistence> logger,
        IOptions<Configuration.DashboardOptions> dashboardOptions)
    {
        _logger = logger;
        _options = options.Value;
        _contextFactory = contextFactory;
        _otlpContext = new OtlpContext
        {
            Logger = logger,
            Options = dashboardOptions.Value.TelemetryLimits
        };

        _logger.LogInformation("SqlServerTelemetryPersistence initialized (ConnectionString configured: {HasConnectionString})",
            !string.IsNullOrEmpty(_options.ConnectionString));
    }

    public async Task AddLogAsync(OtlpLogEntry log, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbcontext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (dbcontext.ConfigureAwait(false))
            {
                var entity = MapLogToEntity(log);
                await dbcontext.Logs.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                await dbcontext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding log to SQL Server");
            throw;
        }
    }

    public async Task AddLogsAsync(IEnumerable<OtlpLogEntry> logs, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var entities = logs.Select(MapLogToEntity);
                await context.Logs.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding logs to SQL Server");
            throw;
        }
    }

    public async Task<PagedResult<OtlpLogEntry>> GetLogsAsync(GetLogsContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                var query = dbContext.Logs.AsQueryable();

                // Filter by resource if specified
                if (context.ResourceKey.HasValue)
                {
                    query = query.Where(l =>
                        l.ResourceName == context.ResourceKey.Value.Name &&
                        (context.ResourceKey.Value.InstanceId == null || l.InstanceId == context.ResourceKey.Value.InstanceId));
                }

                // Apply filters
                foreach (var filter in context.Filters)
                {
                    // Note: This is a simplified filter implementation
                    // You may need to expand this based on the actual TelemetryFilter structure
                    var filterText = filter.ToString() ?? string.Empty;
                    query = query.Where(l => l.Message.Contains(filterText));
                }

                var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
                var takeCount = Math.Min(context.Count, totalCount - context.StartIndex);
                var logs = await query
                    .OrderByDescending(l => l.TimeStamp)
                    .Skip(context.StartIndex)
                    .Take(takeCount)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                // Note: Reconstructing OtlpLogEntry from database entities requires OtlpContext, 
                // OtlpResourceView, and OtlpScope which are not available in the persistence layer.
                // The TelemetryRepository.GetLogs method already implements a fallback mechanism:
                // it tries to get data from persistence first, and if that returns empty results,
                // it falls back to the in-memory cache. This design allows persistence to be
                // write-only (for long-term storage) while reads come from the in-memory cache.
                // 
                // To fully support database reads, you would need to:
                // 1. Store serialized OtlpContext configuration in the database
                // 2. Rebuild OtlpResource and OtlpResourceView from stored data
                // 3. Reconstruct OtlpScope from stored scope names and attributes
                // 4. Deserialize and restore all attributes, events, and relationships
                //
                // For now, return empty results to trigger the fallback to in-memory cache.
                _logger.LogDebug("Database query returned {Count} logs, but reconstruction is not implemented. Falling back to in-memory cache.", totalCount);
                return PagedResult<OtlpLogEntry>.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving logs from SQL Server");
            return PagedResult<OtlpLogEntry>.Empty;
        }
    }

    public async Task<OtlpLogEntry?> GetLogAsync(long logId, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var entity = await context.Logs
                    .FirstOrDefaultAsync(l => l.InternalId == logId, cancellationToken).ConfigureAwait(false);

                if (entity != null)
                {
                    _logger.LogDebug("Found log {LogId} in database, but reconstruction to OtlpLogEntry is not implemented.", logId);
                }

                // Reconstruction not supported - see GetLogsAsync for detailed explanation.
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving log {LogId} from SQL Server", logId);
            return null;
        }
    }

    public async Task AddTraceAsync(OtlpTrace trace, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var entity = MapTraceToEntity(trace);
                await context.Traces.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding trace {TraceId} to SQL Server", trace.TraceId);
            throw;
        }
    }

    public async Task<GetTracesResponse> GetTracesAsync(GetTracesRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var query = context.Traces.Include(t => t.Spans).AsQueryable();

                // Filter by resource if specified
                if (request.ResourceKey.HasValue)
                {
                    query = query.Where(t =>
                        t.ResourceName == request.ResourceKey.Value.Name &&
                        (request.ResourceKey.Value.InstanceId == null || t.InstanceId == request.ResourceKey.Value.InstanceId));
                }

                // Filter by text
                if (!string.IsNullOrEmpty(request.FilterText))
                {
                    query = query.Where(t => t.FullName.Contains(request.FilterText) || t.TraceId.Contains(request.FilterText));
                }

                var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

                var traces = await query
                    .OrderByDescending(t => t.TimeStamp)
                    .Skip(request.StartIndex)
                    .Take(request.Count > 0 ? request.Count : 100)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                var maxDuration = traces.Any() ? traces.Max(t => t.Duration) : TimeSpan.Zero;

                // Reconstruct OtlpTrace objects from database entities
                var otlpTraces = new List<OtlpTrace>();
                foreach (var traceEntity in traces)
                {
                    try
                    {
                        var otlpTrace = MapEntityToTrace(traceEntity);
                        otlpTraces.Add(otlpTrace);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reconstruct trace {TraceId} from database", traceEntity.TraceId);
                    }
                }

                _logger.LogDebug("Successfully reconstructed {Count} of {Total} traces from database", otlpTraces.Count, totalCount);

                return new GetTracesResponse
                {
                    PagedResult = new PagedResult<OtlpTrace>
                    {
                        Items = otlpTraces,
                        TotalItemCount = totalCount,
                        IsFull = totalCount > request.StartIndex + request.Count
                    },
                    MaxDuration = maxDuration
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving traces from SQL Server");
            return new GetTracesResponse
            {
                PagedResult = PagedResult<OtlpTrace>.Empty,
                MaxDuration = TimeSpan.Zero
            };
        }
    }

    public async Task<OtlpTrace?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var entity = await context.Traces
                    .Include(t => t.Spans)
                    .FirstOrDefaultAsync(t => t.TraceId == traceId, cancellationToken).ConfigureAwait(false);

                if (entity != null)
                {
                    _logger.LogDebug("Found trace {TraceId} with {SpanCount} spans in database, but reconstruction to OtlpTrace is not implemented.",
                        traceId, entity.Spans.Count);
                }

                // Reconstruction not supported - see GetTracesAsync for detailed explanation.
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving trace {TraceId} from SQL Server", traceId);
            return null;
        }
    }

    public async Task<OtlpSpan?> GetSpanAsync(string traceId, string spanId, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var entity = await context.Spans
                    .FirstOrDefaultAsync(s => s.TraceId == traceId && s.SpanId == spanId, cancellationToken).ConfigureAwait(false);

                if (entity != null)
                {
                    _logger.LogDebug("Found span {SpanId} in trace {TraceId} in database, but reconstruction to OtlpSpan is not implemented.",
                        spanId, traceId);
                }

                // Reconstruction not supported - see GetTracesAsync for detailed explanation.
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving span {SpanId} from trace {TraceId} in SQL Server", spanId, traceId);
            return null;
        }
    }

    public async Task AddMetricsAsync(OtlpResource resource, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var entity = new MetricEntity
                {
                    ResourceName = resource.ResourceName,
                    InstanceId = resource.InstanceId,
                    TimeStamp = DateTime.UtcNow,
                    Data = JsonSerializer.Serialize(resource) // Simplified serialization
                };

                await context.Metrics.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding metrics for resource {ResourceName} to SQL Server", resource.ResourceName);
            throw;
        }
    }

    public async Task ClearLogsAsync(ResourceKey? resourceKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                if (resourceKey.HasValue)
                {
                    await context.Logs
                        .Where(l => l.ResourceName == resourceKey.Value.Name &&
                                   (resourceKey.Value.InstanceId == null || l.InstanceId == resourceKey.Value.InstanceId))
                        .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await context.Logs.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing logs from SQL Server");
            throw;
        }
    }

    public async Task ClearTracesAsync(ResourceKey? resourceKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                if (resourceKey.HasValue)
                {
                    await context.Traces
                        .Where(t => t.ResourceName == resourceKey.Value.Name &&
                                   (resourceKey.Value.InstanceId == null || t.InstanceId == resourceKey.Value.InstanceId))
                        .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await context.Traces.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing traces from SQL Server");
            throw;
        }
    }

    public async Task ClearMetricsAsync(ResourceKey? resourceKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                if (resourceKey.HasValue)
                {
                    await context.Metrics
                        .Where(m => m.ResourceName == resourceKey.Value.Name &&
                                   (resourceKey.Value.InstanceId == null || m.InstanceId == resourceKey.Value.InstanceId))
                        .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await context.Metrics.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing metrics from SQL Server");
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private static LogEntity MapLogToEntity(OtlpLogEntry log)
    {
        return new LogEntity
        {
            InternalId = log.InternalId,
            ResourceName = log.ResourceView.Resource.ResourceName,
            InstanceId = log.ResourceView.Resource.InstanceId,
            TimeStamp = log.TimeStamp,
            Severity = log.Severity.ToString(),
            Message = log.Message,
            TraceId = log.TraceId,
            SpanId = log.SpanId,
            ParentId = log.ParentId,
            OriginalFormat = log.OriginalFormat,
            ScopeName = log.Scope.Name,
            Attributes = JsonSerializer.Serialize(log.Attributes),
            Flags = log.Flags
        };
    }

    private static TraceEntity MapTraceToEntity(OtlpTrace trace)
    {
        return new TraceEntity
        {
            TraceId = trace.TraceId,
            FullName = trace.FullName,
            ResourceName = trace.FirstSpan.Source.Resource.ResourceName,
            InstanceId = trace.FirstSpan.Source.Resource.InstanceId,
            TimeStamp = trace.TimeStamp,
            Duration = trace.Duration,
            Spans = trace.Spans.Select(MapSpanToEntity).ToList(),
            LastUpdatedDate= trace.LastUpdatedDate,
        };
    }

    private static SpanEntity MapSpanToEntity(OtlpSpan span)
    {
        return new SpanEntity
        {
            TraceId = span.TraceId,
            SpanId = span.SpanId,
            ParentSpanId = span.ParentSpanId,
            Name = span.Name,
            Kind = span.Kind.ToString(),
            StartTime = span.StartTime,
            EndTime = span.EndTime,
            Status = span.Status.ToString(),
            StatusMessage = span.StatusMessage,
            State = span.State,
            Attributes = JsonSerializer.Serialize(span.Attributes),
            Events = JsonSerializer.Serialize(span.Events),
            Links = JsonSerializer.Serialize(span.Links),
            ScopeName = span.Scope.Name
        };
    }

    private OtlpTrace MapEntityToTrace(TraceEntity traceEntity)
    {
        // Create trace with TraceId converted to bytes
        var traceIdBytes = Convert.FromHexString(traceEntity.TraceId);
        var trace = new OtlpTrace(traceIdBytes, DateTime.UtcNow);

        // Convert all spans
        foreach (var spanEntity in traceEntity.Spans.OrderBy(s => s.StartTime))
        {
            var span = MapEntityToSpan(spanEntity, trace, traceEntity.ResourceName, traceEntity.InstanceId);
            trace.AddSpan(span, skipLastUpdatedDate: true);
        }

        return trace;
    }

    private OtlpSpan MapEntityToSpan(SpanEntity spanEntity, OtlpTrace trace, string resourceName, string? instanceId)
    {
        // Get or create resource
        var resourceKey = new ResourceKey(resourceName, instanceId);
        if (!_resourceCache.TryGetValue(resourceKey, out var resource))
        {
            resource = new OtlpResource(resourceName, instanceId, uninstrumentedPeer: false, _otlpContext);
            _resourceCache[resourceKey] = resource;
        }

        // Get or create scope
        if (!_scopeCache.TryGetValue(spanEntity.ScopeName, out var scope))
        {
            scope = new OtlpScope(spanEntity.ScopeName, string.Empty, []);
            _scopeCache[spanEntity.ScopeName] = scope;
        }

        // Create resource view with empty attributes (simplified)
        var resourceView = resource.GetView([]);

        // Deserialize attributes
        var attributes = string.IsNullOrEmpty(spanEntity.Attributes)
            ? []
            : JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(spanEntity.Attributes) ?? [];

        // Deserialize events
        var events = string.IsNullOrEmpty(spanEntity.Events)
            ? new List<OtlpSpanEvent>()
            : JsonSerializer.Deserialize<List<OtlpSpanEvent>>(spanEntity.Events) ?? new List<OtlpSpanEvent>();

        // Deserialize links
        var links = string.IsNullOrEmpty(spanEntity.Links)
            ? new List<OtlpSpanLink>()
            : JsonSerializer.Deserialize<List<OtlpSpanLink>>(spanEntity.Links) ?? new List<OtlpSpanLink>();

        // Parse Kind
        var kind = Enum.TryParse<OtlpSpanKind>(spanEntity.Kind, out var parsedKind) ? parsedKind : OtlpSpanKind.Unspecified;

        // Parse Status
        var status = Enum.TryParse<OtlpSpanStatusCode>(spanEntity.Status, out var parsedStatus) ? parsedStatus : OtlpSpanStatusCode.Unset;

        // Create span
        var span = new OtlpSpan(resourceView, trace, scope)
        {
            SpanId = spanEntity.SpanId,
            ParentSpanId = spanEntity.ParentSpanId,
            Name = spanEntity.Name,
            Kind = kind,
            StartTime = spanEntity.StartTime,
            EndTime = spanEntity.EndTime,
            Status = status,
            StatusMessage = spanEntity.StatusMessage,
            State = spanEntity.State,
            Attributes = attributes,
            Events = events,
            Links = links,
            BackLinks = new List<OtlpSpanLink>()
        };

        return span;
    }
}

/// <summary>
/// Configuration options for SQL Server telemetry persistence.
/// </summary>
public sealed class SqlServerTelemetryOptions
{
    /// <summary>
    /// Gets or sets the SQL Server connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to automatically create the database if it doesn't exist.
    /// </summary>
    public bool EnsureCreated { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to automatically apply migrations.
    /// </summary>
    public bool AutoMigrate { get; set; }

    /// <summary>
    /// Gets or sets the command timeout in seconds for database operations.
    /// </summary>
    public int CommandTimeout { get; set; } = 30;
}
