using Application.DependencyInjection;
using Application.Services;
using Api.Services;
using Infrastructure.Common;
using Infrastructure.Database;
using Infrastructure.Mqtt.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMqtt(builder.Configuration);

builder.Services
    .AddOptions<MockRuntimeOptions>()
    .Bind(builder.Configuration.GetSection(MockRuntimeOptions.SectionName));

builder.Services.AddSingleton<MockSensorRuntimeManager>();
builder.Services.AddSingleton<IMockSensorRuntimeNotifier>(sp =>
    sp.GetRequiredService<MockSensorRuntimeManager>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<MockSensorRuntimeManager>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapOpenApi();
app.UseSwaggerUI(x => x.SwaggerEndpoint("/openapi/v1.json", "OpenAPI v1"));

app.UseHttpsRedirection();
app.UseExceptionHandler();
app.MapControllers();

app.Run();
