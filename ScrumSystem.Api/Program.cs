using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;
using ScrumSystem.Api.Routes;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<DatabaseContext>();
builder.Services.AddSingleton<AppDataStore>();

// Configure JSON serialization to include null values and use camelCase
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize database
var dbContext = app.Services.GetRequiredService<DatabaseContext>();
dbContext.Initialize();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapAuthRoutes();
app.MapUserRoutes();
app.MapProjectRoutes();
app.MapSprintRoutes();
app.MapStoryRoutes();
app.MapTaskRoutes();
app.MapStandupRoutes();
app.MapDashboardRoutes();
app.MapNotificationRoutes();

app.Run();
