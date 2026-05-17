using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class DashboardRoutes
{
    public static void MapDashboardRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard");

        group.MapGet("/stats", async (DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var stats = new DashboardStats
            {
                TotalProjects = await CountAsync(connection, "SELECT COUNT(*) FROM Projects"),
                ActiveSprints = await CountAsync(connection, "SELECT COUNT(*) FROM Sprints WHERE Status = 'Active'"),
                TotalStories = await CountAsync(connection, "SELECT COUNT(*) FROM UserStories"),
                TotalTasks = await CountAsync(connection, "SELECT COUNT(*) FROM Tasks"),
                CompletedTasks = await CountAsync(connection, "SELECT COUNT(*) FROM Tasks WHERE Status = 'Done'")
            };

            stats.PendingTasks = stats.TotalTasks - stats.CompletedTasks;
            return Results.Ok(stats);
        });

        group.MapGet("/projects/{projectId}/stats", async (string projectId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var stats = new Dictionary<string, object>
            {
                ["totalProjects"] = 1,
                ["totalSprints"] = await CountAsync(connection, "SELECT COUNT(*) FROM Sprints WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId", projectId),
                ["activeSprints"] = await CountAsync(connection, "SELECT COUNT(*) FROM Sprints WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId AND Status = 'Active'", projectId),
                ["totalStories"] = await CountAsync(connection, "SELECT COUNT(*) FROM UserStories WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId", projectId),
                ["backlogStories"] = await CountAsync(connection, "SELECT COUNT(*) FROM UserStories WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId AND SprintId IS NULL", projectId),
                ["completedStories"] = await CountAsync(connection, "SELECT COUNT(*) FROM UserStories WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId AND Status = 'Done'", projectId),
                ["totalTasks"] = await CountAsync(connection, @"
                    SELECT COUNT(*)
                    FROM Tasks t
                    INNER JOIN UserStories us ON t.StoryId = us.Id
                    WHERE CAST(us.ProjectId AS NVARCHAR(36)) = @ProjectId", projectId),
                ["completedTasks"] = await CountAsync(connection, @"
                    SELECT COUNT(*)
                    FROM Tasks t
                    INNER JOIN UserStories us ON t.StoryId = us.Id
                    WHERE CAST(us.ProjectId AS NVARCHAR(36)) = @ProjectId AND t.Status = 'Done'", projectId),
                ["totalStoryPoints"] = await SumAsync(connection, @"
                    SELECT COALESCE(SUM(StoryPoints), 0)
                    FROM UserStories
                    WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId", projectId),
                ["activeSprintPoints"] = await SumAsync(connection, @"
                    SELECT COALESCE(SUM(us.StoryPoints), 0)
                    FROM UserStories us
                    INNER JOIN Sprints s ON us.SprintId = s.Id
                    WHERE CAST(us.ProjectId AS NVARCHAR(36)) = @ProjectId AND s.Status = 'Active'", projectId)
            };

            return Results.Ok(stats);
        });
    }

    private static async Task<int> CountAsync(SqlConnection connection, string sql, string? projectId = null)
    {
        using var cmd = new SqlCommand(sql, connection);
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            cmd.Parameters.AddWithValue("@ProjectId", projectId);
        }

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> SumAsync(SqlConnection connection, string sql, string projectId)
    {
        using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@ProjectId", projectId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
