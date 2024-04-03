// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Authentication.OtlpConnection;

namespace Aspire.Dashboard.Model;

internal sealed class ContentSecurityPolicyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _content;

    public ContentSecurityPolicyMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        _next = next;

        // Based on Blazor documentation recommendations:
        // https://learn.microsoft.com/aspnet/core/blazor/security/content-security-policy#server-side-blazor-apps
        // Changes:
        // - style-src adds inline styles as they're used extensively by Blazor FluentUI.
        // - frame-src none added to prevent nesting in iframe.
        var content = "base-uri 'self'; " +
            "img-src data: https:; " +
            "object-src 'none'; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "frame-src 'none';";

        // default-src limits where content can fetched from.
        // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Content-Security-Policy/default-src
        // This value stops BrowserLink and automatic hot reload from working in development.
        // Add this value for all non-development environments.
        if (!environment.IsDevelopment())
        {
            content += " default-src 'self';";
        }

        _content = content;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Don't set CSP on OTLP requests.
        if (context.Features.Get<IOtlpConnectionFeature>() == null)
        {
            context.Response.Headers.ContentSecurityPolicy = _content;
        }

        return _next(context);
    }
}
