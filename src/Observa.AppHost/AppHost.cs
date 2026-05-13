var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Session)
    .WithEnvironment("POSTGRES_DB", "observadb")
    .WithBindMount(
        source: Path.Combine(AppContext.BaseDirectory, "sql"),
        target: "/docker-entrypoint-initdb.d",
        isReadOnly: true);

var observaDb = postgres.AddDatabase("observadb");

builder.AddProject<Projects.Observa_Web>("web")
    .WithReference(observaDb)
    .WaitFor(observaDb);

builder.Build().Run();
