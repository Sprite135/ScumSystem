using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class TaskRoutes
{
    public static void MapTaskRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks");

        group.MapPost("/", (CreateTaskRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                if (store.Data.UserStories.All(story => story.Id != request.StoryId))
                {
                    return Results.BadRequest("La historia no existe");
                }

                var task = new TaskItem
                {
                    Id = Guid.NewGuid().ToString(),
                    StoryId = request.StoryId,
                    Title = request.Title.Trim(),
                    Description = request.Description?.Trim(),
                    EstimatedHours = request.EstimatedHours,
                    Status = "Todo",
                    CreatedAt = DateTime.UtcNow
                };

                store.Data.Tasks.Add(task);
                store.Save();
                return Results.Created($"/api/tasks/{task.Id}", ToTaskDto(task, store));
            }
        });

        group.MapGet("/story/{storyId}", (string storyId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                return Results.Ok(store.Data.Tasks
                    .Where(task => task.StoryId == storyId)
                    .OrderBy(task => task.CreatedAt)
                    .Select(task => ToTaskDto(task, store))
                    .ToList());
            }
        });

        group.MapPatch("/{id}/status", (string id, UpdateTaskStatusRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var task = store.Data.Tasks.FirstOrDefault(item => item.Id == id);
                if (task is null)
                {
                    return Results.NotFound();
                }

                task.Status = request.Status;
                task.ActualHours = request.ActualHours;
                task.CompletedAt = request.Status == "Done" ? DateTime.UtcNow : null;
                store.Save();
                return Results.Ok(new { message = "Status updated" });
            }
        });

        group.MapPatch("/{id}/assign", (string id, string assignedTo, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var task = store.Data.Tasks.FirstOrDefault(item => item.Id == id);
                if (task is null)
                {
                    return Results.NotFound();
                }

                task.AssignedToId = assignedTo;
                store.Save();
                return Results.Ok(new { message = "Task assigned" });
            }
        });

        group.MapGet("/board/{sprintId}", (string sprintId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var storyIds = store.Data.UserStories.Where(story => story.SprintId == sprintId).Select(story => story.Id).ToHashSet();
                var tasks = store.Data.Tasks.Where(task => storyIds.Contains(task.StoryId)).Select(task => ToTaskDto(task, store)).ToList();
                var board = new KanbanBoardDto
                {
                    Todo = tasks.Where(task => task.Status == "Todo").ToList(),
                    InProgress = tasks.Where(task => task.Status == "InProgress").ToList(),
                    Done = tasks.Where(task => task.Status == "Done").ToList(),
                    Blocked = tasks.Where(task => task.Status == "Blocked").ToList()
                };

                return Results.Ok(board);
            }
        });

        group.MapGet("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var task = store.Data.Tasks.FirstOrDefault(item => item.Id == id);
                return task is null ? Results.NotFound() : Results.Ok(ToTaskDto(task, store));
            }
        });

        group.MapPut("/{id}", (string id, CreateTaskRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var task = store.Data.Tasks.FirstOrDefault(item => item.Id == id);
                if (task is null)
                {
                    return Results.NotFound();
                }

                task.StoryId = request.StoryId;
                task.Title = request.Title.Trim();
                task.Description = request.Description?.Trim();
                task.EstimatedHours = request.EstimatedHours;
                store.Save();
                return Results.Ok(new { message = "Tarea actualizada exitosamente" });
            }
        });

        group.MapDelete("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var task = store.Data.Tasks.FirstOrDefault(item => item.Id == id);
                if (task is null)
                {
                    return Results.NotFound();
                }

                store.Data.Tasks.Remove(task);
                store.Save();
                return Results.Ok(new { message = "Tarea eliminada exitosamente" });
            }
        });
    }

    private static TaskItemDto ToTaskDto(TaskItem task, AppDataStore store)
    {
        var assignedUser = store.Data.Users.FirstOrDefault(user => user.Id == task.AssignedToId);
        var story = store.Data.UserStories.FirstOrDefault(item => item.Id == task.StoryId);

        return new TaskItemDto
        {
            Id = task.Id,
            StoryId = task.StoryId,
            Title = task.Title,
            Description = task.Description,
            EstimatedHours = task.EstimatedHours,
            ActualHours = task.ActualHours,
            Status = task.Status,
            AssignedToId = task.AssignedToId,
            AssignedToName = assignedUser?.Name,
            StoryTitle = story?.Title,
            CreatedAt = task.CreatedAt,
            CompletedAt = task.CompletedAt
        };
    }
}

public class KanbanBoardDto
{
    public List<TaskItemDto> Todo { get; set; } = new();
    public List<TaskItemDto> InProgress { get; set; } = new();
    public List<TaskItemDto> Done { get; set; } = new();
    public List<TaskItemDto> Blocked { get; set; } = new();
}
