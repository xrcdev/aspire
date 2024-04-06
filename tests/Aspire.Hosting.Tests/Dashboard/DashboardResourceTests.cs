// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.Dcp;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aspire.Hosting.Tests.Dashboard;

public class DashboardResourceTests
{
    [Fact]
    public async Task DashboardIsAutomaticallyAddedAsHiddenResource()
    {
        using var builder = TestDistributedApplicationBuilder.Create();

        builder.Services.Configure<DcpOptions>(o =>
        {
            o.DashboardPath = "dashboard";
        });

        var app = builder.Build();

        await app.ExecuteBeforeStartHooksAsync(default);

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        var dashboard = Assert.Single(model.Resources);
        var initialSnapshot = Assert.Single(dashboard.Annotations.OfType<ResourceSnapshotAnnotation>());

        Assert.NotNull(dashboard);
        Assert.Equal("aspire-dashboard", dashboard.Name);
        Assert.Equal("Hidden", initialSnapshot.InitialSnapshot.State);
    }
}
