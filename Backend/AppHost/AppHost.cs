var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddContainer("postgres", "postgres:16-alpine")
    .WithEnvironment("POSTGRES_DB", "tcc")
    .WithEnvironment("POSTGRES_USER", "postgres")
    .WithEnvironment("POSTGRES_PASSWORD", "postgres")
    .WithEndpoint(port: 5432, targetPort: 5432, isExternal: true);

var redis = builder
    .AddContainer("redis", "redis:7-alpine")
    .WithEndpoint(port: 6379, targetPort: 6379, isExternal: true);

var mosquitto = builder
    .AddContainer("mosquitto", "eclipse-mosquitto:alpine")
    .WithBindMount(
        Path.Combine(builder.Environment.ContentRootPath, "mosquitto.conf"),
        "/mosquitto/config/mosquitto.conf")
    .WithEndpoint(port: 1883, targetPort: 1883, scheme: "mqtt", isExternal: true);

var defaultDbConnection = "Host=postgres;Port=5432;Database=tcc;Username=postgres;Password=postgres";

builder.AddProject<Projects.Consumer>("consumer")
    .WithEnvironment("ConnectionStrings__Redis", "redis:6379")
    .WithEnvironment("ConnectionStrings__DefaultConnection", defaultDbConnection)
    .WithEnvironment("Mqtt__Broker", "mosquitto")
    .WithEnvironment("Mqtt__Port", "1883")
    .WaitFor(redis)
    .WaitFor(postgres)
    .WaitFor(mosquitto);

builder.AddProject<Projects.Api>("api")
    .WithEnvironment("ConnectionStrings__Redis", "redis:6379")
    .WithEnvironment("ConnectionStrings__DefaultConnection", defaultDbConnection)
    .WaitFor(redis)
    .WaitFor(postgres);


builder.Build().Run();
