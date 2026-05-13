var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var observaDb = postgres.AddDatabase("observadb");

builder.AddProject<Projects.Observa_Web>("web")
    .WithReference(observaDb)
    .WaitFor(observaDb);

builder.Build().Run();
