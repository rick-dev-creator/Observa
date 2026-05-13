using Crucible.Chains.DependencyInjection;
using Observa.Web.Components;

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
