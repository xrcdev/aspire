// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Otlp.Storage;
using Microsoft.EntityFrameworkCore;

namespace Aspire.Dashboard.Extensions;

/// <summary>
/// Extension methods for registering telemetry persistence services
/// </summary>
public static class TelemetryPersistenceExtensions
{
    /// <summary>
    /// Adds SQL Server-based telemetry persistence to the service collection
    /// </summary>
    public static IServiceCollection AddSqlServerTelemetryPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SqlServerTelemetry") 
            ?? configuration["SqlServerTelemetry:ConnectionString"];

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server connection string not found. " +
                "Please configure 'ConnectionStrings:SqlServerTelemetry' or 'SqlServerTelemetry:ConnectionString' in appsettings.json");
        }

        services.Configure<SqlServerTelemetryOptions>(configuration.GetSection("SqlServerTelemetry"));
        
        services.AddDbContextFactory<SqlServerTelemetryDbContext>(options =>
            options.UseSqlServer(connectionString));
        
        services.AddSingleton<ITelemetryPersistence, SqlServerTelemetryPersistence>();
        
        return services;
    }

    /// <summary>
    /// Adds SQL Server-based telemetry persistence with custom options
    /// </summary>
    public static IServiceCollection AddSqlServerTelemetryPersistence(
        this IServiceCollection services,
        string connectionString,
        Action<SqlServerTelemetryOptions>? configureOptions = null)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString));
        }

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        
        services.AddDbContextFactory<SqlServerTelemetryDbContext>(options =>
            options.UseSqlServer(connectionString));
        
        services.AddSingleton<ITelemetryPersistence, SqlServerTelemetryPersistence>();
        
        return services;
    }
}
