var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Consumer>("consumer");

builder.Build().Run();
