# SQLite Telemetry Persistence

This document describes how to use SQLite for persisting telemetry data (logs, traces, and metrics) in the Aspire Dashboard.

## Overview

The SQLite telemetry persistence feature allows you to store telemetry data in a lightweight SQLite database file. This is useful for:

- Local development and testing
- Lightweight deployments without requiring a SQL Server instance
- Scenarios where data needs to persist across Dashboard restarts
- Compliance and audit trail requirements

## Configuration

### 1. Enable SQLite Persistence

Add the following configuration to your `appsettings.json` or use the provided `appsettings.Sqlite.json`:

```json
{
  "ConnectionStrings": {
    "SqliteTelemetry": "Data Source=AspireTelemetry.db"
  },
  "SqliteTelemetry": {
    "Enabled": true,
    "EnsureCreated": true,
    "AutoMigrate": false,
    "CommandTimeout": 30
  }
}
```

### 2. Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `ConnectionStrings:SqliteTelemetry` | SQLite connection string | Required |
| `SqliteTelemetry:Enabled` | Enable/disable SQLite persistence | `false` |
| `SqliteTelemetry:EnsureCreated` | Auto-create database if it doesn't exist | `true` |
| `SqliteTelemetry:AutoMigrate` | Apply EF Core migrations on startup | `false` |
| `SqliteTelemetry:CommandTimeout` | Database command timeout in seconds | `30` |

### 3. Connection String Examples

**Basic file database:**
```
Data Source=AspireTelemetry.db
```

**With absolute path:**
```
Data Source=C:\Data\AspireTelemetry.db
```

**In-memory database (not recommended for production):**
```
Data Source=:memory:
```

**With WAL mode for better concurrent access:**
```
Data Source=AspireTelemetry.db;Mode=ReadWriteCreate;Cache=Shared
```

## Database Schema

The SQLite database contains the following tables:

### Logs Table
Stores structured log entries with resource information, severity, message, trace correlation, and attributes.

### Traces Table
Stores trace metadata including trace ID, resource name, timestamps, and duration.

### Spans Table
Stores individual spans with their attributes, events, links, and timing information.

### Metrics Table
Stores metrics data in JSON format for flexible metric types.

## Usage Notes

### Write-Only Persistence

This implementation provides **write-only persistence**:
- All telemetry data is written to SQLite for long-term storage
- Read operations return empty results, falling back to the in-memory cache
- This design ensures real-time Dashboard performance while maintaining persistence

### Priority with SQL Server

If both `SqlServerTelemetry:Enabled` and `SqliteTelemetry:Enabled` are set to `true`, SQL Server takes priority.

### Data Location

By default, the database file is created in the current working directory. You can specify an absolute path in the connection string for better control.

## Example

To run the Dashboard with SQLite persistence:

```bash
dotnet run --project src/Aspire.Dashboard -- --configuration:SqliteTelemetry:Enabled=true
```

Or use the configuration file:

```bash
dotnet run --project src/Aspire.Dashboard -- --environment Sqlite
```

This will load `appsettings.Sqlite.json` configuration.

## Comparison: SQLite vs SQL Server

| Feature | SQLite | SQL Server |
|---------|--------|------------|
| Setup complexity | Low (file-based) | High (server required) |
| Concurrent access | Limited | High |
| Performance | Good for small/medium data | Better for large datasets |
| Data analysis tools | Limited | Rich ecosystem |
| Deployment | Self-contained | Separate infrastructure |

Choose SQLite for development/testing and lightweight deployments. Choose SQL Server for production environments with high concurrency requirements.
