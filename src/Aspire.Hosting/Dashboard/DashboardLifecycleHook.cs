// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dcp;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aspire.Hosting.Dashboard;

internal sealed class DashboardLifecycleHook(IConfiguration configuration,
                                             IOptions<DcpOptions> dcpOptions,
                                             DistributedApplicationOptions distributedApplicationOptions,
                                             ILogger<DistributedApplication> distributedApplicationLogger,
                                             IDashboardEndpointProvider dashboardEndpointProvider) : IDistributedApplicationLifecycleHook
{
    private readonly string? _dashboardPath = dcpOptions.Value.DashboardPath;

    public Task BeforeStartAsync(DistributedApplicationModel model, CancellationToken cancellationToken)
    {
        if (distributedApplicationOptions.DisableDashboard)
        {
            return Task.CompletedTask;
        }

        if (model.Resources.SingleOrDefault(r => StringComparers.ResourceName.Equals(r.Name, KnownResourceNames.AspireDashboard)) is { } dashboardResource)
        {
            ConfigureAspireDashboardResource(dashboardResource);

            // Make it first in the list
            model.Resources.Remove(dashboardResource);
            model.Resources.Insert(0, dashboardResource);
        }
        else
        {
            AddDashboardResource(model);
        }

        return Task.CompletedTask;
    }

    private void AddDashboardResource(DistributedApplicationModel model)
    {
        if (_dashboardPath is not { } dashboardPath)
        {
            throw new DistributedApplicationException("Dashboard path empty or file does not exist.");
        }

        var fullyQualifiedDashboardPath = Path.GetFullPath(dashboardPath);
        var dashboardWorkingDirectory = Path.GetDirectoryName(fullyQualifiedDashboardPath);

        ExecutableResource? dashboardResource = default;

        if (string.Equals(".dll", Path.GetExtension(fullyQualifiedDashboardPath), StringComparison.OrdinalIgnoreCase))
        {
            // The dashboard path is a DLL, so run it with `dotnet <dll>`

            dashboardResource = new ExecutableResource(KnownResourceNames.AspireDashboard, "dotnet", dashboardWorkingDirectory ?? "");

            dashboardResource.Annotations.Add(new CommandLineArgsCallbackAnnotation(args =>
            {
                args.Add(fullyQualifiedDashboardPath);
            }));
        }
        else
        {
            // Assume the dashboard path is directly executable
            dashboardResource = new ExecutableResource(KnownResourceNames.AspireDashboard, fullyQualifiedDashboardPath, dashboardWorkingDirectory ?? "");
        }

        ConfigureAspireDashboardResource(dashboardResource);

        model.Resources.Insert(0, dashboardResource);
    }

    private void PrintDashboardUrls()
    {
        if (distributedApplicationOptions.DisableDashboard)
        {
            return;
        }
        // We just check the HTTP endpoint because this will prove that the
        // dashboard is listening and is ready to process requests.
        if (configuration["ASPNETCORE_URLS"] is not { } dashboardUrls)
        {
            throw new DistributedApplicationException("Cannot check dashboard availability since ASPNETCORE_URLS environment variable not set.");
        }

        if (StringUtils.TryGetUriFromDelimitedString(dashboardUrls, ";", out var firstDashboardUrl))
        {
            distributedApplicationLogger.LogInformation("Now listening on: {DashboardUrl}", firstDashboardUrl.ToString().TrimEnd('/'));
        }
    }

    private void ConfigureAspireDashboardResource(IResource dashboardResource)
    {
        // Don't publish the resource to the manifest.
        dashboardResource.Annotations.Add(ManifestPublishingCallbackAnnotation.Ignore);

        // Remove endpoint annotations because we are directly configuring
        // the dashboard app (it doesn't go through the proxy!).
        var endpointAnnotations = dashboardResource.Annotations.OfType<EndpointAnnotation>().ToList();
        foreach (var endpointAnnotation in endpointAnnotations)
        {
            dashboardResource.Annotations.Remove(endpointAnnotation);
        }

        dashboardResource.Annotations.Add(new EnvironmentCallbackAnnotation(async context =>
        {
            var dashboardUrls = configuration["ASPNETCORE_URLS"];

            if (string.IsNullOrEmpty(dashboardUrls))
            {
                throw new DistributedApplicationException("Failed to configure dashboard resource because ASPNETCORE_URLS environment variable was not set.");
            }

            if (configuration["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"] is not { } otlpEndpointUrl)
            {
                throw new DistributedApplicationException("Failed to configure dashboard resource because DOTNET_DASHBOARD_OTLP_ENDPOINT_URL environment variable was not set.");
            }

            var resourceServiceUrl = await dashboardEndpointProvider.GetResourceServiceUriAsync(context.CancellationToken).ConfigureAwait(false);

            var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production";

            context.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = environment;
            context.EnvironmentVariables[DashboardConfigNames.DashboardFrontendUrlName.EnvVarName] = dashboardUrls;
            context.EnvironmentVariables[DashboardConfigNames.ResourceServiceUrlName.EnvVarName] = resourceServiceUrl;
            context.EnvironmentVariables[DashboardConfigNames.DashboardOtlpUrlName.EnvVarName] = otlpEndpointUrl;
            context.EnvironmentVariables[DashboardConfigNames.ResourceServiceAuthModeName.EnvVarName] = "Unsecured";

            if (configuration["AppHost:BrowserToken"] is { Length: > 0 } browserToken)
            {
                context.EnvironmentVariables[DashboardConfigNames.DashboardFrontendAuthModeName.EnvVarName] = "BrowserToken";
                context.EnvironmentVariables[DashboardConfigNames.DashboardFrontendBrowserTokenName.EnvVarName] = browserToken;
            }
            else
            {
                context.EnvironmentVariables[DashboardConfigNames.DashboardFrontendAuthModeName.EnvVarName] = "Unsecured";
            }

            if (configuration["AppHost:OtlpApiKey"] is { Length: > 0 } otlpApiKey)
            {
                context.EnvironmentVariables[DashboardConfigNames.DashboardOtlpAuthModeName.EnvVarName] = "ApiKey";
                context.EnvironmentVariables[DashboardConfigNames.DashboardOtlpPrimaryApiKeyName.EnvVarName] = otlpApiKey;
            }
            else
            {
                context.EnvironmentVariables[DashboardConfigNames.DashboardOtlpAuthModeName.EnvVarName] = "Unsecured";
            }

            PrintDashboardUrls();
        }));
    }
}
