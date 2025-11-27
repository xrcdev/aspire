# SQL Server Telemetry Persistence Implementation

This document describes the SQL Server implementation of the `ITelemetryPersistence` interface for persisting OpenTelemetry data to a SQL Server database using Entity Framework Core.

## Overview

The implementation consists of three main components:

1. **SqlServerTelemetryDbContext** - Entity Framework Core DbContext
2. **Entity Models** - Database entity classes for Logs, Traces, Spans, and Metrics
3. **SqlServerTelemetryPersistence** - Implementation of `ITelemetryPersistence` interface

## Dependencies

This implementation requires the `Microsoft.EntityFrameworkCore.SqlServer` NuGet package, which is already added to the `Aspire.Dashboard.csproj` via PackageReference.

## Files

- `SqlServerTelemetryDbContext.cs` - DbContext and entity model definitions
- `SqlServerTelemetryPersistence.cs` - Main implementation and options class

## Database Schema

The implementation creates the following tables:

### Logs Table
- Stores individual log entries
- Indexed on `InternalId`, `ResourceName/InstanceId/TimeStamp`, and `TraceId`
- Attributes stored as JSON

### Traces Table  
- Stores trace information
- Unique index on `TraceId`
- One-to-many relationship with Spans (cascade delete)

### Spans Table
- Stores span data within traces
- Unique index on `TraceId` + `SpanId`
- Foreign key to Traces table

### Metrics Table
- Stores metric data
- Indexed on `ResourceName/InstanceId/TimeStamp`
- Data stored as JSON

## Configuration

### Using appsettings.json (Recommended)

The Dashboard automatically uses SQL Server persistence when enabled in configuration:

```json
{
  "ConnectionStrings": {
    "SqlServerTelemetry": "Server=localhost;Database=AspireTelemetry;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "SqlServerTelemetry": {
    "Enabled": true,
    "EnsureCreated": true,
    "AutoMigrate": false,
    "CommandTimeout": 30
  }
}
```

**Note**: When `SqlServerTelemetry:Enabled` is `true`, the Dashboard automatically registers `SqlServerTelemetryPersistence` as the `ITelemetryPersistence` implementation, replacing the default in-memory storage.

### Manual Configuration (Advanced)

If you need more control, you can use the extension methods directly:

```csharp
// Option 1: Use configuration
builder.Services.AddSqlServerTelemetryPersistence(builder.Configuration);

// Option 2: Provide connection string directly
builder.Services.AddSqlServerTelemetryPersistence(
    connectionString: "Server=localhost;Database=AspireTelemetry;...",
    configureOptions: options =>
    {
        options.EnsureCreated = true;
        options.CommandTimeout = 60;
    });
```

## Configuration Options

### SqlServerTelemetryOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| ConnectionString | string | empty | SQL Server connection string |
| EnsureCreated | bool | true | Automatically create database if it doesn't exist on startup |
| AutoMigrate | bool | false | Automatically apply EF Core migrations on startup |
| CommandTimeout | int | 30 | Database command timeout in seconds |

**Important**: The database is automatically initialized during application startup when `SqlServerTelemetry:Enabled` is `true`:
- If `EnsureCreated` is `true` (default), the database and all tables will be created automatically if they don't exist
- If `AutoMigrate` is `true`, EF Core migrations will be applied instead
- You don't need to manually create the database or run migration scripts

## Usage

The implementation is used through the `ITelemetryPersistence` interface:

```csharp
public class TelemetryService
{
    private readonly ITelemetryPersistence _persistence;

    public TelemetryService(ITelemetryPersistence persistence)
    {
        _persistence = persistence;
    }

    public async Task StoreLogs(IEnumerable<OtlpLogEntry> logs)
    {
        await _persistence.AddLogsAsync(logs);
    }

    public async Task<PagedResult<OtlpLogEntry>> GetLogs(GetLogsContext context)
    {
        return await _persistence.GetLogsAsync(context);
    }
}
```

## Quick Start

1. **Configure the connection string** in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "SqlServerTelemetry": "Server=localhost;Database=AspireTelemetry;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "SqlServerTelemetry": {
    "Enabled": true
  }
}
```

2. **Run the application** - The database and tables will be created automatically on first startup.

3. **View telemetry data** - All logs, traces, and metrics are now persisted to SQL Server and will survive application restarts.

## Future Database Support

The implementation is designed to be extended for other databases:

1. Create a new DbContext class (e.g., `PostgreSqlTelemetryDbContext`)
2. Create a new persistence implementation (e.g., `PostgreSqlTelemetryPersistence`)
3. Register the appropriate implementation in DI based on configuration

Example:

```csharp
var databaseType = builder.Configuration["Telemetry:DatabaseType"]; // "SqlServer", "PostgreSQL", "MySQL"

switch (databaseType)
{
    case "SqlServer":
        builder.Services.AddSqlServerTelemetryPersistence(builder.Configuration);
        break;
    case "PostgreSQL":
        builder.Services.AddPostgreSqlTelemetryPersistence(builder.Configuration);
        break;
    // Add more database providers as needed
}
```

## Known Limitations and TODOs

1. **Mapping from Entities to Domain Models** - The `GetLogsAsync`, `GetTraceAsync`, and `GetSpanAsync` methods currently return empty/null results. Full reconstruction of `OtlpLogEntry`, `OtlpTrace`, and `OtlpSpan` from database entities requires:
   - Access to `OtlpContext` for proper initialization
   - Reconstruction of `OtlpResourceView` and `OtlpScope` objects
   - Deserialization of JSON attributes

2. **ConfigureAwait** - All async operations should add `.ConfigureAwait(false)` for better performance in non-UI contexts

3. **Filter Implementation** - The `GetLogsAsync` filter logic is simplified and should be expanded based on the actual `TelemetryFilter` structure

4. **Performance Optimization**:
   - Consider adding pagination for large result sets
   - Implement caching for frequently accessed data
   - Add indexes for common query patterns

5. **Migration Support** - Consider creating EF Core migrations for production deployments instead of using `EnsureCreated()`

## Testing

To test the implementation:

1. Configure a SQL Server instance (LocalDB, SQL Server Express, or full SQL Server)
2. Update connection string in configuration
3. Run the application - the database will be created automatically if `EnsureCreated` is true
4. Verify tables are created using SQL Server Management Studio or Azure Data Studio
5. Use the persistence methods to store and retrieve telemetry data

## Performance Considerations

- **Batch Operations**: Use `AddLogsAsync` for bulk inserts instead of multiple `AddLogAsync` calls
- **Indexing**: The default indexes cover common query patterns, add more as needed based on usage
- **Connection Pooling**: Entity Framework Core uses connection pooling by default
- **Async/Await**: All operations are async for better scalability

## Security

- Always use parameterized queries (handled by EF Core)
- Use Integrated Security or secure credential storage for connection strings
- Consider encryption for sensitive telemetry data
- Implement proper authorization checks before querying data

## Migration from Other Implementations

To migrate from the stub `ElasticsearchTelemetryPersistence` or other implementations:

1. Export existing data (if any)
2. Update DI registration to use `SqlServerTelemetryPersistence`
3. Configure connection string
4. Initialize database
5. Import data (if needed)

The interface remains the same, so no code changes are required in consumers of `ITelemetryPersistence`.
