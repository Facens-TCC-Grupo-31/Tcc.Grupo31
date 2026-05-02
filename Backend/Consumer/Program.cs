using Application.DependencyInjection;
using Consumer;
using Infrastructure.Common;
using Infrastructure.Mqtt.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMqtt(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
