using Crucible.Chains.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Observa.Connectors.Abstractions;
using Observa.Features.Connectors.Manual;
using Observa.Features.Connectors.Orchestration;
using Observa.Features.Connectors.Recurring;
using Observa.Features.Connectors.Registry;
using Observa.Features.Streams.Aggregates;
using Observa.Features.Streams.Services;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Observa.Integration.Tests;

public sealed class ObservaTestClusterFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = default!;
    public IServiceProvider ServiceProvider => Cluster.Client.ServiceProvider;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await Cluster.StopAllSilosAsync();
        Cluster.Dispose();
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder silo)
        {
            silo.AddMemoryGrainStorageAsDefault()
                .UseInMemoryReminderService();

            silo.Services
                .AddCrucible()
                .AddStreamAggregate()
                .AddSingleton<IConnector, ManualConnector>()
                .AddSingleton<IConnector, RecurringConnector>()
                .AddSingleton<IConnectorRegistry, ConnectorRegistry>()
                .AddSingleton<ConnectorPollOrchestrator>();
        }
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder client)
        {
            client.Services
                .AddCrucible()
                .AddStreamAggregate()
                .AddScoped<StreamService>()
                .AddSingleton<IConnector, ManualConnector>()
                .AddSingleton<IConnector, RecurringConnector>()
                .AddSingleton<IConnectorRegistry, ConnectorRegistry>();
        }
    }
}
