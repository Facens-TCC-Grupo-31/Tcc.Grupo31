using Application.Common.DependencyInjection;
using Consumer;

var builder = Host.CreateApplicationBuilder(args);

var redisConn = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Redis connection string was not provided.");

builder.Services.AddApplicationServices(redisConn);

builder.Services.AddApplicationDbContext(
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string was not provided."));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
