// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;

namespace Aspire.Dashboard.Otlp.Storage;

/// <summary>
/// Entity Framework Core DbContext for SQLite telemetry persistence.
/// </summary>
public sealed class SqliteTelemetryDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteTelemetryDbContext"/> class.
    /// </summary>
    public SqliteTelemetryDbContext(DbContextOptions<SqliteTelemetryDbContext> options)
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
            // SQLite uses TEXT for large strings
            entity.Property(e => e.Attributes).HasColumnType("TEXT");
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
            entity.Property(e => e.Attributes).HasColumnType("TEXT");
            entity.Property(e => e.Events).HasColumnType("TEXT");
            entity.Property(e => e.Links).HasColumnType("TEXT");
        });

        modelBuilder.Entity<MetricEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ResourceName, e.InstanceId, e.TimeStamp });
            entity.Property(e => e.Data).HasColumnType("TEXT");
        });
    }
}
