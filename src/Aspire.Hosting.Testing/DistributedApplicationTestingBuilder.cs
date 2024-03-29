// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Diagnostics;
using System.Reflection;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aspire.Hosting.Testing;

/// <summary>
/// Methods for creating distributed application instances for testing purposes.
/// </summary>
public static class DistributedApplicationTestingBuilder
{
    /// <summary>
    /// Creates a new instance of <see cref="DistributedApplicationTestingBuilder"/>.
    /// </summary>
    /// <typeparam name="TEntryPoint">
    /// A type in the entry point assembly of the target Aspire AppHost. Typically, the Program class can be used.
    /// </typeparam>
    /// <returns>
    /// A new instance of <see cref="DistributedApplicationTestingBuilder"/>.
    /// </returns>
    public static async Task<IDistributedApplicationTestingBuilder> CreateAsync<TEntryPoint>(
        Action<DistributedApplicationOptions, HostApplicationBuilderSettings> configureBuilder,
        CancellationToken cancellationToken = default)
        where TEntryPoint : class
    {
        var factory = new SuspendingDistributedApplicationFactory((_, __) => { }, assembly: typeof(TEntryPoint).Assembly);
        return await factory.CreateBuilderAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new instance of <see cref="DistributedApplicationTestingBuilder"/>.
    /// </summary>
    /// <typeparam name="TEntryPoint">
    /// A type in the entry point assembly of the target Aspire AppHost. Typically, the Program class can be used.
    /// </typeparam>
    /// <returns>
    /// A new instance of <see cref="DistributedApplicationTestingBuilder"/>.
    /// </returns>
    public static async Task<IDistributedApplicationTestingBuilder> CreateAsync<TEntryPoint>(CancellationToken cancellationToken = default) where TEntryPoint : class
    {
        var factory = new SuspendingDistributedApplicationFactory((_, __) => { }, assembly: typeof(TEntryPoint).Assembly);
        return await factory.CreateBuilderAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new instance of <see cref="DistributedApplicationTestingBuilder"/>.
    /// </summary>
    /// <returns>
    /// A new instance of <see cref="DistributedApplicationTestingBuilder"/>.
    /// </returns>
    public static IDistributedApplicationTestingBuilder Create(
        Action<DistributedApplicationOptions, HostApplicationBuilderSettings>? configureBuilder = null,
        string? assemblyName = null)
    {
        return new TestingBuilder(configureBuilder, assemblyName ?? string.Empty);
    }

    private sealed class TestingBuilder : IDistributedApplicationTestingBuilder
    {
        private readonly DistributedApplicationBuilder _innerBuilder;
        private bool _didBuild;
        private bool _disposedValue;

        public TestingBuilder(Action<DistributedApplicationOptions, HostApplicationBuilderSettings>? configureOptions, string assemblyName)
        {
            _innerBuilder = BuilderInterceptor.CreateBuilder(Configure);

            _innerBuilder.Services.AddHttpClient();
            _innerBuilder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

            void Configure(DistributedApplicationOptions applicationOptions, HostApplicationBuilderSettings hostBuilderOptions)
            {
                hostBuilderOptions.EnvironmentName = Environments.Development;
                hostBuilderOptions.ApplicationName = assemblyName;
                applicationOptions.AssemblyName = assemblyName;
                applicationOptions.DisableDashboard = true;
                var cfg = hostBuilderOptions.Configuration ??= new();
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DcpPublisher:RandomizePorts"] = "true",
                    ["DcpPublisher:DeleteResourcesOnShutdown"] = "true",
                    ["DcpPublisher:ResourceNameSuffix"] = $"{Random.Shared.Next():x}",
                });

                configureOptions?.Invoke(applicationOptions, hostBuilderOptions);
            }
        }

        public ConfigurationManager Configuration => _innerBuilder.Configuration;

        public string AppHostDirectory => _innerBuilder.AppHostDirectory;

        public IHostEnvironment Environment => _innerBuilder.Environment;

        public IServiceCollection Services => _innerBuilder.Services;

        public DistributedApplicationExecutionContext ExecutionContext => _innerBuilder.ExecutionContext;

        public IResourceCollection Resources => _innerBuilder.Resources;

        public IResourceBuilder<T> AddResource<T>(T resource) where T : IResource => _innerBuilder.AddResource(resource);

        public DistributedApplication Build()
        {
            try
            {
                return _innerBuilder.Build();
            }
            finally
            {
                _didBuild = true;
            }
        }

        public Task<DistributedApplication> BuildAsync(CancellationToken cancellationToken = default) => Task.FromResult(Build());

        public IResourceBuilder<T> CreateResourceBuilder<T>(T resource) where T : IResource
        {
            return _innerBuilder.CreateResourceBuilder(resource);
        }

        public void Dispose()
        {
            if (!_disposedValue)
            {
                _disposedValue = true;
                if (!_didBuild)
                {
                    try
                    {
                        using var app = Build();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private sealed class BuilderInterceptor : IObserver<DiagnosticListener>
        {
            private static readonly ThreadLocal<BuilderInterceptor?> s_currentListener = new();
            private readonly ApplicationBuilderDiagnosticListener _applicationBuilderListener;
            private readonly Action<DistributedApplicationOptions, HostApplicationBuilderSettings>? _onConstructing;

            private BuilderInterceptor(Action<DistributedApplicationOptions, HostApplicationBuilderSettings>? onConstructing)
            {
                _onConstructing = onConstructing;
                _applicationBuilderListener = new(this);
            }

            public static DistributedApplicationBuilder CreateBuilder(Action<DistributedApplicationOptions, HostApplicationBuilderSettings> onConstructing)
            {
                var interceptor = new BuilderInterceptor(onConstructing);
                var original = s_currentListener.Value;
                s_currentListener.Value = interceptor;
                try
                {
                    using var subscription = DiagnosticListener.AllListeners.Subscribe(interceptor);
                    return new DistributedApplicationBuilder([]);
                }
                finally
                {
                    s_currentListener.Value = original;
                }
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {

            }

            public void OnNext(DiagnosticListener value)
            {
                if (s_currentListener.Value != this)
                {
                    // Ignore events that aren't for this listener
                    return;
                }

                if (value.Name == "Aspire.Hosting")
                {
                    _applicationBuilderListener.Subscribe(value);
                }
            }

            private sealed class ApplicationBuilderDiagnosticListener(BuilderInterceptor owner) : IObserver<KeyValuePair<string, object?>>
            {
                private IDisposable? _disposable;

                public void Subscribe(DiagnosticListener listener)
                {
                    _disposable = listener.Subscribe(this);
                }

                public void OnCompleted()
                {
                    _disposable?.Dispose();
                }

                public void OnError(Exception error)
                {
                }

                public void OnNext(KeyValuePair<string, object?> value)
                {
                    if (s_currentListener.Value != owner)
                    {
                        // Ignore events that aren't for this listener
                        return;
                    }

                    if (value.Key == "DistributedApplicationBuilderConstructing")
                    {
                        var args = ((DistributedApplicationOptions Options, HostApplicationBuilderSettings InnerBuilderOptions))value.Value!;
                        owner._onConstructing?.Invoke(args.Options, args.InnerBuilderOptions);
                    }
                }
            }
        }
    }

    private sealed class SuspendingDistributedApplicationFactory(Action<DistributedApplicationOptions, HostApplicationBuilderSettings> configureBuilder, Assembly? assembly)
        : DistributedApplicationFactory(assembly)
    {
        private readonly SemaphoreSlim _continueBuilding = new(0);

        public async Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(CancellationToken cancellationToken)
        {
            var innerBuilder = await ResolveBuilderAsync(cancellationToken).ConfigureAwait(false);
            return new Builder(this, innerBuilder);
        }

        protected override void OnBuilderCreating(DistributedApplicationOptions applicationOptions, HostApplicationBuilderSettings hostOptions)
        {
            base.OnBuilderCreating(applicationOptions, hostOptions);
            configureBuilder(applicationOptions, hostOptions);
        }

        protected override void OnBuilderCreated(DistributedApplicationBuilder applicationBuilder)
        {
            base.OnBuilderCreated(applicationBuilder);
        }

        protected override void OnBuilding(DistributedApplicationBuilder applicationBuilder)
        {
            base.OnBuilding(applicationBuilder);

            // Wait until the owner signals that building can continue by calling BuildAsync().
            _continueBuilding.Wait();
        }

        public async Task<DistributedApplication> BuildAsync(CancellationToken cancellationToken)
        {
            _continueBuilding.Release();
            return await ResolveApplicationAsync(cancellationToken).ConfigureAwait(false);
        }

        public override async ValueTask DisposeAsync()
        {
            _continueBuilding.Release();
            await base.DisposeAsync().ConfigureAwait(false);
        }

        public override void Dispose()
        {
            _continueBuilding.Release();
            base.Dispose();
        }

        private sealed class Builder(SuspendingDistributedApplicationFactory factory, DistributedApplicationBuilder innerBuilder) : IDistributedApplicationTestingBuilder
        {
            private bool _builtApp;

            public ConfigurationManager Configuration => innerBuilder.Configuration;

            public string AppHostDirectory => innerBuilder.AppHostDirectory;

            public IHostEnvironment Environment => innerBuilder.Environment;

            public IServiceCollection Services => innerBuilder.Services;

            public DistributedApplicationExecutionContext ExecutionContext => innerBuilder.ExecutionContext;

            public IResourceCollection Resources => innerBuilder.Resources;

            public IResourceBuilder<T> AddResource<T>(T resource) where T : IResource => innerBuilder.AddResource(resource);

            public DistributedApplication Build() => BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

            public async Task<DistributedApplication> BuildAsync(CancellationToken cancellationToken)
            {
                _builtApp = true;
                var innerApp = await factory.BuildAsync(cancellationToken).ConfigureAwait(false);
                return new DelegatedDistributedApplication(new DelegatedHost(factory, innerApp));
            }

            public IResourceBuilder<T> CreateResourceBuilder<T>(T resource) where T : IResource => innerBuilder.CreateResourceBuilder(resource);

            public void Dispose()
            {
                // When the builder is disposed we build a host and then dispose it.
                // This cleans up unmanaged resources on the inner builder.
                if (!_builtApp)
                {
                    try
                    {
                        Build().Dispose();
                    }
                    catch
                    {
                        // Ignore errors.
                    }
                }
            }
        }

        private sealed class DelegatedDistributedApplication(DelegatedHost host) : DistributedApplication(host)
        {
            private readonly DelegatedHost _host = host;

            public override async Task RunAsync(CancellationToken cancellationToken)
            {
                // Avoid calling the base here, since it will execute the pre-start hooks
                // before calling the corresponding host method, which also executes the same pre-start hooks.
                await _host.RunAsync(cancellationToken).ConfigureAwait(false);
            }

            public override async Task StartAsync(CancellationToken cancellationToken)
            {
                // Avoid calling the base here, since it will execute the pre-start hooks
                // before calling the corresponding host method, which also executes the same pre-start hooks.
                await _host.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            public override async Task StopAsync(CancellationToken cancellationToken)
            {
                await _host.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private sealed class DelegatedHost(SuspendingDistributedApplicationFactory appFactory, DistributedApplication innerApp) : IHost, IAsyncDisposable
        {
            public IServiceProvider Services => innerApp.Services;

            public void Dispose()
            {
                appFactory.Dispose();
            }

            public async ValueTask DisposeAsync()
            {
                await appFactory.DisposeAsync().ConfigureAwait(false);
            }

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                await appFactory.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            public async Task StopAsync(CancellationToken cancellationToken)
            {
                await appFactory.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// A builder for creating instances of <see cref="DistributedApplication"/> for testing purposes.
/// </summary>
public interface IDistributedApplicationTestingBuilder : IDistributedApplicationBuilder, IDisposable
{
    /// <summary>
    /// Builds and returns a new <see cref="DistributedApplication"/> instance. This can only be called once.
    /// </summary>
    /// <returns>A new <see cref="DistributedApplication"/> instance.</returns>
    Task<DistributedApplication> BuildAsync(CancellationToken cancellationToken = default);
}
