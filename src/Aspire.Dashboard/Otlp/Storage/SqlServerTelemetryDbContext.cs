// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;

namespace Aspire.Dashboard.Otlp.Storage;

/// <summary>
/// Entity Framework Core DbContext for SQL Server telemetry persistence.
/// </summary>
public sealed class SqlServerTelemetryDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerTelemetryDbContext"/> class.
    /// </summary>
    public SqlServerTelemetryDbContext(DbContextOptions<SqlServerTelemetryDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the Logs table.
    /// </summary>
    public DbSet<LogEntity> Logs => Set<LogEntity>();

    /// <summary>
    /// Gets or sets the Traces table.
    /// </summary>
    public DbSet<TraceEntity> Traces => Set<TraceEntity>();

    /// <summary>
    /// Gets or sets the Spans table.
    /// </summary>
    public DbSet<SpanEntity> Spans => Set<SpanEntity>();

    /// <summary>
    /// Gets or sets the Metrics table.
    /// </summary>
    public DbSet<MetricEntity> Metrics => Set<MetricEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LogEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InternalId);
            entity.HasIndex(e => new { e.ResourceName, e.InstanceId, e.TimeStamp });
            entity.HasIndex(e => e.TraceId);
            entity.Property(e => e.Attributes).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<TraceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TraceId).IsUnique();
            entity.HasIndex(e => new { e.ResourceName, e.InstanceId, e.TimeStamp });
            entity.HasMany(e => e.Spans)
                .WithOne()
                .HasForeignKey(s => s.TraceEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpanEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TraceId, e.SpanId }).IsUnique();
            entity.Property(e => e.Attributes).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Events).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Links).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<MetricEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ResourceName, e.InstanceId, e.TimeStamp });
            entity.Property(e => e.Data).HasColumnType("nvarchar(max)");
        });
    }
}

/// <summary>
/// Log entry entity for database storage.
/// </summary>
public sealed class LogEntity
{
    public long Id { get; set; }
    public long InternalId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string? InstanceId { get; set; }
    public DateTime TimeStamp { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? ParentId { get; set; }
    public string? OriginalFormat { get; set; }
    public string ScopeName { get; set; } = string.Empty;
    public string Attributes { get; set; } = string.Empty;
    public uint Flags { get; set; }
}

/// <summary>
/// Trace entity for database storage.
/// </summary>
public sealed class TraceEntity
{
    public long Id { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string? InstanceId { get; set; }
    public DateTime TimeStamp { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
    public List<SpanEntity> Spans { get; set; } = new();
}

/// <summary>
/// Span entity for database storage.
/// </summary>
public sealed class SpanEntity
{
    public long Id { get; set; }
    public long TraceEntityId { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string? ParentSpanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public string? State { get; set; }
    public string Attributes { get; set; } = string.Empty;
    public string Events { get; set; } = string.Empty;
    public string Links { get; set; } = string.Empty;
    public string ScopeName { get; set; } = string.Empty;
}

/// <summary>
/// Metric entity for database storage.
/// </summary>
public sealed class MetricEntity
{
    public long Id { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string? InstanceId { get; set; }
    public DateTime TimeStamp { get; set; }
    public string Data { get; set; } = string.Empty;
}
