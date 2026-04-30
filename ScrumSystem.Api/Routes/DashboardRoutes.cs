using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class DashboardRoutes
{
    public static void MapDashboardRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard");

        // Get dashboard stats
        group.MapGet("/stats", async (DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var stats = new DashboardStats();

            // Total projects
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Projects", conn))
            {
                stats.TotalProjects = (int)await cmd.ExecuteScalarAsync();
            }

            // Active sprints
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Sprints WHERE Status = 'Active'", conn))
            {
                stats.ActiveSprints = (int)await cmd.ExecuteScalarAsync();
            }

            // Total stories
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM UserStories", conn))
            {
                stats.TotalStories = (int)await cmd.ExecuteScalarAsync();
            }

            // Total tasks
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Tasks", conn))
            {
                stats.TotalTasks = (int)await cmd.ExecuteScalarAsync();
            }

            // Completed tasks
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Tasks WHERE Status = 'Done'", conn))
            {
                stats.CompletedTasks = (int)await cmd.ExecuteScalarAsync();
            }

            stats.PendingTasks = stats.TotalTasks - stats.CompletedTasks;

            return Results.Ok(stats);
        });

        // Get project stats
        group.MapGet("/projects/{projectId:guid}/stats", async (Guid projectId, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var stats = new Dictionary<string, object>();

            // Total sprints
            using (var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM Sprints WHERE ProjectId = @ProjectId", conn))
            {
                cmd.Parameters.AddWithValue("@ProjectId", projectId);
                stats["totalSprints"] = (int)await cmd.ExecuteScalarAsync();
            }

            // Active sprints
            using (var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM Sprints WHERE ProjectId = @ProjectId AND Status = 'Active'", conn))
            {
                cmd.Parameters.AddWithValue("@ProjectId", projectId);
                stats["activeSprints"] = (int)await cmd.ExecuteScalarAsync();
            }

            // Backlog stories
            using (var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM UserStories WHERE ProjectId = @ProjectId AND Status = 'Backlog'", conn))
            {
                cmd.Parameters.AddWithValue("@ProjectId", projectId);
                stats["backlogStories"] = (int)await cmd.ExecuteScalarAsync();
            }

            // Total story points in active sprint
            using (var cmd = new SqlCommand(@"
                SELECT ISNULL(SUM(StoryPoints), 0) 
                FROM UserStories us
                JOIN Sprints s ON us.SprintId = s.Id
                WHERE s.ProjectId = @ProjectId AND s.Status = 'Active'", conn))
            {
                cmd.Parameters.AddWithValue("@ProjectId", projectId);
                stats["activeSprintPoints"] = (int)await cmd.ExecuteScalarAsync();
            }

            return Results.Ok(stats);
        });
    }
}
