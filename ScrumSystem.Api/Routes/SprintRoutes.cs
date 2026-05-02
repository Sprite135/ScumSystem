using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class SprintRoutes
{
    public static void MapSprintRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sprints");

        group.MapGet("/", (AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                return Results.Ok(store.Data.Sprints
                    .OrderByDescending(sprint => sprint.CreatedAt)
                    .Select(sprint => ToSprintDto(sprint, store))
                    .ToList());
            }
        });

        group.MapGet("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var sprint = store.Data.Sprints.FirstOrDefault(item => item.Id == id);
                return sprint is null ? Results.NotFound() : Results.Ok(ToSprintDto(sprint, store));
            }
        });

        group.MapGet("/project/{projectId}", (string projectId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                return Results.Ok(store.Data.Sprints
                    .Where(sprint => sprint.ProjectId == projectId)
                    .OrderByDescending(sprint => sprint.StartDate)
                    .Select(sprint => ToSprintDto(sprint, store))
                    .ToList());
            }
        });

        group.MapGet("/{id}/burndown", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var sprint = store.Data.Sprints.FirstOrDefault(item => item.Id == id);
                if (sprint is null)
                {
                    return Results.NotFound();
                }

                var stories = store.Data.UserStories
                    .Where(story => story.SprintId == id)
                    .ToList();

                var startDate = sprint.StartDate.Date;
                var endDate = sprint.EndDate.Date < startDate ? startDate : sprint.EndDate.Date;
                var totalPoints = stories.Sum(story => story.StoryPoints ?? 0);
                var totalDays = Math.Max(1, (endDate - startDate).Days);

                var chart = new BurndownChartDto();
                for (var date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    chart.Labels.Add(date.ToString("dd/MM"));
                    var elapsedDays = (date - startDate).Days;
                    var idealRemaining = totalPoints - ((decimal)totalPoints * elapsedDays / totalDays);
                    chart.Ideal.Add(Math.Max(0, Math.Round(idealRemaining, 2)));

                    var remaining = stories
                        .Where(story => story.Status != "Done" || !story.UpdatedAt.HasValue || story.UpdatedAt.Value.Date > date)
                        .Sum(story => story.StoryPoints ?? 0);

                    chart.Actual.Add(remaining);
                }

                return Results.Ok(chart);
            }
        });

        group.MapPost("/", (CreateSprintRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                if (store.Data.Projects.All(project => project.Id != request.ProjectId))
                {
                    return Results.BadRequest("El proyecto no existe");
                }

                var sprint = new Sprint
                {
                    Id = Guid.NewGuid().ToString(),
                    ProjectId = request.ProjectId,
                    Name = request.Name.Trim(),
                    Goal = request.Goal?.Trim(),
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    DurationWeeks = Math.Max(1, (int)Math.Ceiling((request.EndDate.Date - request.StartDate.Date).TotalDays / 7d)),
                    Status = "Planning",
                    CreatedAt = DateTime.UtcNow
                };

                store.Data.Sprints.Add(sprint);
                store.Save();
                return Results.Created($"/api/sprints/{sprint.Id}", ToSprintDto(sprint, store));
            }
        });

        group.MapPut("/{id}", (string id, UpdateStatusRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var sprint = store.Data.Sprints.FirstOrDefault(item => item.Id == id);
                if (sprint is null)
                {
                    return Results.NotFound();
                }

                sprint.Status = string.IsNullOrWhiteSpace(request.Status) ? sprint.Status : request.Status;
                sprint.UpdatedAt = DateTime.UtcNow;
                store.Save();

                return Results.Ok(new { message = "Sprint actualizado" });
            }
        });

        group.MapDelete("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var sprint = store.Data.Sprints.FirstOrDefault(item => item.Id == id);
                if (sprint is null)
                {
                    return Results.NotFound();
                }

                foreach (var story in store.Data.UserStories.Where(story => story.SprintId == id))
                {
                    story.SprintId = null;
                    story.Status = "Backlog";
                    story.UpdatedAt = DateTime.UtcNow;
                }

                store.Data.StandupNotes.RemoveAll(note => note.SprintId == id);
                store.Data.Sprints.Remove(sprint);
                store.Save();
                return Results.Ok(new { message = "Sprint eliminado" });
            }
        });
    }

    private static SprintDto ToSprintDto(Sprint sprint, AppDataStore store)
    {
        var sprintStories = store.Data.UserStories.Where(story => story.SprintId == sprint.Id).ToList();
        var sprintStoryIds = sprintStories.Select(story => story.Id).ToHashSet();
        var sprintTasks = store.Data.Tasks.Where(task => sprintStoryIds.Contains(task.StoryId)).ToList();

        return new SprintDto
        {
            Id = sprint.Id,
            ProjectId = sprint.ProjectId,
            Name = sprint.Name,
            Goal = sprint.Goal,
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate,
            DurationWeeks = sprint.DurationWeeks,
            Status = sprint.Status,
            CreatedAt = sprint.CreatedAt,
            UpdatedAt = sprint.UpdatedAt,
            TotalStoryPoints = sprintStories.Sum(story => story.StoryPoints ?? 0),
            CompletedStoryPoints = sprintStories.Where(story => story.Status == "Done").Sum(story => story.StoryPoints ?? 0),
            TotalTasks = sprintTasks.Count,
            CompletedTasks = sprintTasks.Count(task => task.Status == "Done")
        };
    }
}
