using Application.DependencyInjection;
using Infrastructure.Common;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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
