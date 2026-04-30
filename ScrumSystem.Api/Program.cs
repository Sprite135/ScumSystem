using ScrumSystem.Api.Models;
using ScrumSystem.Api.Routes;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<DatabaseContext>();

// Configure JSON serialization to include null values and use camelCase
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
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

// Configure middleware
app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize database
var dbContext = app.Services.GetRequiredService<DatabaseContext>();
dbContext.Initialize();

// Map API routes
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
