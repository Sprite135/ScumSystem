using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class DashboardRoutes
{
    public static void MapDashboardRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard");

        group.MapGet("/stats", (AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var stats = new DashboardStats
                {
                    TotalProjects = store.Data.Projects.Count,
                    ActiveSprints = store.Data.Sprints.Count(sprint => sprint.Status == "Active"),
                    TotalStories = store.Data.UserStories.Count,
                    TotalTasks = store.Data.Tasks.Count,
                    CompletedTasks = store.Data.Tasks.Count(task => task.Status == "Done")
                };

                stats.PendingTasks = stats.TotalTasks - stats.CompletedTasks;
                return Results.Ok(stats);
            }
        });

        group.MapGet("/projects/{projectId}/stats", (string projectId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var sprintIds = store.Data.Sprints.Where(sprint => sprint.ProjectId == projectId).Select(sprint => sprint.Id).ToHashSet();
                var activeSprintIds = store.Data.Sprints.Where(sprint => sprint.ProjectId == projectId && sprint.Status == "Active").Select(sprint => sprint.Id).ToHashSet();
                var stats = new Dictionary<string, object>
                {
                    ["totalSprints"] = sprintIds.Count,
                    ["activeSprints"] = activeSprintIds.Count,
                    ["backlogStories"] = store.Data.UserStories.Count(story => story.ProjectId == projectId && story.Status == "Backlog"),
                    ["activeSprintPoints"] = store.Data.UserStories
                        .Where(story => story.ProjectId == projectId && story.SprintId != null && activeSprintIds.Contains(story.SprintId))
                        .Sum(story => story.StoryPoints ?? 0)
                };

                return Results.Ok(stats);
            }
        });
    }
}
