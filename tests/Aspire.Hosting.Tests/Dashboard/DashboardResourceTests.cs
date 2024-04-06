// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.Dcp;
using Aspire.Hosting.Tests.Utils;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aspire.Hosting.Tests.Dashboard;

public class DashboardResourceTests
{
    [Fact]
    public async Task DashboardIsAutomaticallyAddedAsHiddenResource()
    {
        using var builder = TestDistributedApplicationBuilder.Create();

        var app = builder.Build();

        await app.ExecuteBeforeStartHooksAsync(default);

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var dashboard = Assert.Single(model.Resources);
        var initialSnapshot = Assert.Single(dashboard.Annotations.OfType<ResourceSnapshotAnnotation>());

        Assert.NotNull(dashboard);
        Assert.Equal("aspire-dashboard", dashboard.Name);
        Assert.Equal("Hidden", initialSnapshot.InitialSnapshot.State);
    }

    [Fact]
    public async Task DashboardAuthConfigured_EnvVarsPresent()
    {
        // Arrange
        using var builder = TestDistributedApplicationBuilder.Create();

        builder.Services.AddSingleton<IDashboardEndpointProvider, MockDashboardEndpointProvider>();

        builder.Configuration.Sources.Clear();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_URLS"] = "http://localhost",
            ["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"] = "http://localhost",
            ["AppHost:BrowserToken"] = "TestBrowserToken!",
            ["AppHost:OtlpApiKey"] = "TestOtlpApiKey!"
        });

        var app = builder.Build();

        await app.ExecuteBeforeStartHooksAsync(default);

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var dashboard = Assert.Single(model.Resources);

        var config = await EnvironmentVariableEvaluator.GetEnvironmentVariablesAsync(dashboard);

        Assert.Equal("BrowserToken", config.Single(e => e.Key == DashboardConfigNames.DashboardFrontendAuthModeName.EnvVarName).Value);
        Assert.Equal("TestBrowserToken!", config.Single(e => e.Key == DashboardConfigNames.DashboardFrontendBrowserTokenName.EnvVarName).Value);

        Assert.Equal("ApiKey", config.Single(e => e.Key == DashboardConfigNames.DashboardOtlpAuthModeName.EnvVarName).Value);
        Assert.Equal("TestOtlpApiKey!", config.Single(e => e.Key == DashboardConfigNames.DashboardOtlpPrimaryApiKeyName.EnvVarName).Value);
    }

    [Fact]
    public async Task DashboardAuthRemoved_EnvVarsUnsecured()
    {
        // Arrange
        using var builder = TestDistributedApplicationBuilder.Create();

        builder.Services.AddSingleton<IDashboardEndpointProvider, MockDashboardEndpointProvider>();

        builder.Configuration.Sources.Clear();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_URLS"] = "http://localhost",
            ["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"] = "http://localhost"
        });

        var app = builder.Build();

        await app.ExecuteBeforeStartHooksAsync(default);

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var dashboard = Assert.Single(model.Resources);

        var config = await EnvironmentVariableEvaluator.GetEnvironmentVariablesAsync(dashboard);

        Assert.Equal("Unsecured", config.Single(e => e.Key == DashboardConfigNames.DashboardFrontendAuthModeName.EnvVarName).Value);
        Assert.Equal("Unsecured", config.Single(e => e.Key == DashboardConfigNames.DashboardOtlpAuthModeName.EnvVarName).Value);
    }

    [Fact]
    public async Task DashboardResourceServiceUriIsSet()
    {
        // Arrange
        using var builder = TestDistributedApplicationBuilder.Create();

        builder.Services.AddSingleton<IDashboardEndpointProvider, MockDashboardEndpointProvider>();

        builder.Configuration.Sources.Clear();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_URLS"] = "http://localhost",
            ["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"] = "http://localhost"
        });

        var app = builder.Build();

        await app.ExecuteBeforeStartHooksAsync(default);

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var dashboard = Assert.Single(model.Resources);

        var config = await EnvironmentVariableEvaluator.GetEnvironmentVariablesAsync(dashboard);

        Assert.Equal("http://localhost:5000", config.Single(e => e.Key == DashboardConfigNames.ResourceServiceUrlName.EnvVarName).Value);
    }

    private sealed class MockDashboardEndpointProvider : IDashboardEndpointProvider
    {
        public Task<string> GetResourceServiceUriAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult("http://localhost:5000");
        }
    }

}
