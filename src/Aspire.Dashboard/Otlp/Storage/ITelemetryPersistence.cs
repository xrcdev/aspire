// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//using Aspire.Dashboard.Model;
using Aspire.Dashboard.Otlp.Model;

namespace Aspire.Dashboard.Otlp.Storage;

/// <summary>
/// Interface for persisting and retrieving telemetry data from external storage
/// </summary>
public interface ITelemetryPersistence
{
    /// <summary>
    /// Persists a log entry to storage
    /// </summary>
    Task AddLogAsync(OtlpLogEntry log, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists multiple log entries to storage
    /// </summary>
    Task AddLogsAsync(IEnumerable<OtlpLogEntry> logs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves logs from storage based on context
    /// </summary>
    Task<PagedResult<OtlpLogEntry>> GetLogsAsync(GetLogsContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single log by ID
    /// </summary>
    Task<OtlpLogEntry?> GetLogAsync(long logId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a trace to storage
    /// </summary>
    Task AddTraceAsync(OtlpTrace trace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves traces from storage based on request
    /// </summary>
    Task<GetTracesResponse> GetTracesAsync(GetTracesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single trace by ID
    /// </summary>
    Task<OtlpTrace?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single span from a trace
    /// </summary>
    Task<OtlpSpan?> GetSpanAsync(string traceId, string spanId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists metrics data
    /// </summary>
    Task AddMetricsAsync(OtlpResource resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears logs for a specific resource or all logs if resourceKey is null
    /// </summary>
    Task ClearLogsAsync(ResourceKey? resourceKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears traces for a specific resource or all traces if resourceKey is null
    /// </summary>
    Task ClearTracesAsync(ResourceKey? resourceKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears metrics for a specific resource or all metrics if resourceKey is null
    /// </summary>
    Task ClearMetricsAsync(ResourceKey? resourceKey = null, CancellationToken cancellationToken = default);
}
