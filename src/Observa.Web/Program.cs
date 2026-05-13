using System.Data.Common;
using Crucible.Chains.DependencyInjection;
using Npgsql;
using Observa.Components;
using Observa.Connectors.Abstractions;
using Observa.Connectors.Patreon;
using Observa.Features.Connectors.Catalog;
using Observa.Features.Seed;
using Observa.Features.Connectors.Manual;
using Observa.Features.Connectors.Orchestration;
using Observa.Features.Connectors.Recurring;
using Observa.Features.Connectors.Registry;
using Observa.Features.Streams.Aggregates;
using Observa.Features.Streams.Services;

DbProviderFactories.RegisterFactory("Npgsql", NpgsqlFactory.Instance);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.UseOrleans(silo =>
{
    var connectionString = builder.Configuration.GetConnectionString("observadb")
        ?? throw new InvalidOperationException("Missing connection string 'observadb'.");

    silo.UseLocalhostClustering()
        .AddAdoNetGrainStorageAsDefault(opts =>
        {
            opts.Invariant = "Npgsql";
            opts.ConnectionString = connectionString;
        })
        .UseAdoNetReminderService(opts =>
        {
            opts.Invariant = "Npgsql";
            opts.ConnectionString = connectionString;
        });
});

builder.Services.AddCrucible();
builder.Services.AddStreamAggregate();
builder.Services.AddScoped<StreamService>();
builder.Services.AddScoped<StreamQueryService>();
builder.Services.AddScoped<StreamAnalyticsService>();
builder.Services.AddSingleton<ConnectorCatalogService>();

builder.Services.AddSingleton<IConnector, ManualConnector>();
builder.Services.AddSingleton<IConnector, RecurringConnector>();
builder.Services.AddPatreonConnectors(builder.Configuration);
builder.Services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
builder.Services.AddSingleton<ConnectorPollOrchestrator>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<StreamSeedService>();
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
