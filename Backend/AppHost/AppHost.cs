var builder = DistributedApplication.CreateBuilder(args);

var mosquitto = builder
    .AddContainer("mosquitto", "eclipse-mosquitto:alpine")
    .WithBindMount(
        Path.Combine(builder.Environment.ContentRootPath, "mosquitto.conf"),
        "/mosquitto/config/mosquitto.conf")
    .WithEndpoint(port: 1883, targetPort: 1883, scheme: "mqtt", isExternal: true);


builder.AddProject<Projects.Consumer>("consumer");


builder.Build().Run();
