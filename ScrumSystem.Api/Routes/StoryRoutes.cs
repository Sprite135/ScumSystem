using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class StoryRoutes
{
    public static void MapStoryRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stories");

        group.MapGet("/", (AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                return Results.Ok(store.Data.UserStories
                    .OrderByDescending(story => story.CreatedAt)
                    .Select(story => ToStoryDto(story, store))
                    .ToList());
            }
        });

        group.MapGet("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var story = store.Data.UserStories.FirstOrDefault(item => item.Id == id);
                return story is null ? Results.NotFound() : Results.Ok(ToStoryDto(story, store));
            }
        });

        group.MapGet("/project/{projectId}", (string projectId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                return Results.Ok(store.Data.UserStories
                    .Where(story => story.ProjectId == projectId)
                    .OrderByDescending(story => story.CreatedAt)
                    .Select(story => ToStoryDto(story, store))
                    .ToList());
            }
        });

        group.MapGet("/project/{projectId}/backlog", (string projectId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                return Results.Ok(store.Data.UserStories
                    .Where(story => story.ProjectId == projectId && string.IsNullOrWhiteSpace(story.SprintId))
                    .OrderByDescending(story => story.CreatedAt)
                    .Select(story => ToStoryDto(story, store))
                    .ToList());
            }
        });

        group.MapGet("/project/{projectId}/board", (string projectId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var members = store.Data.ProjectMembers
                    .Where(member => member.ProjectId == projectId)
                    .Join(store.Data.Users, member => member.UserId, user => user.Id, (member, user) => new ProjectMemberDto
                    {
                        Id = user.Id,
                        Name = user.Name,
                        Email = user.Email,
                        Role = member.Role
                    })
                    .OrderBy(member => member.Name)
                    .ToList();

                var activeSprintIds = store.Data.Sprints
                    .Where(sprint => sprint.ProjectId == projectId && sprint.Status == "Active")
                    .Select(sprint => sprint.Id)
                    .ToHashSet();

                var sprintStoryQuery = store.Data.UserStories
                    .Where(story => story.ProjectId == projectId
                        && !string.IsNullOrWhiteSpace(story.SprintId)
                        && activeSprintIds.Contains(story.SprintId!));

                var stories = sprintStoryQuery
                    .OrderByDescending(story => story.UpdatedAt ?? story.CreatedAt)
                    .Select(story =>
                    {
                        var assignee = store.Data.Users.FirstOrDefault(user => user.Id == story.AssigneeId);
                        return new BoardStoryDto
                        {
                            Id = story.Id,
                            ProjectId = story.ProjectId,
                            SprintId = story.SprintId,
                            Title = story.Title,
                            Description = story.Description,
                            StoryPoints = story.StoryPoints,
                            Priority = story.Priority,
                            Status = story.Status,
                            AssigneeId = story.AssigneeId,
                            AssigneeName = assignee?.Name
                        };
                    })
                    .ToList();

                return Results.Ok(new BoardDataDto
                {
                    Stories = stories,
                    Members = members,
                    HasActiveSprint = activeSprintIds.Count > 0
                });
            }
        });

        group.MapGet("/sprint/{sprintId}", (string sprintId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                return Results.Ok(store.Data.UserStories
                    .Where(story => story.SprintId == sprintId)
                    .OrderByDescending(story => story.CreatedAt)
                    .Select(story => ToStoryDto(story, store))
                    .ToList());
            }
        });

        group.MapPost("/", (CreateStoryRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                if (store.Data.Projects.All(project => project.Id != request.ProjectId))
                {
                    return Results.BadRequest("El proyecto no existe");
                }

                var project = store.Data.Projects.First(project => project.Id == request.ProjectId);
                var storyNumber = store.Data.UserStories.Count(story => story.ProjectId == request.ProjectId) + 1;
                var story = new UserStory
                {
                    Id = Guid.NewGuid().ToString(),
                    ProjectId = request.ProjectId,
                    SprintId = request.SprintId,
                    Title = request.Title.Trim(),
                    Description = request.Description?.Trim(),
                    AcceptanceCriteria = request.AcceptanceCriteria?.Trim(),
                    StoryPoints = request.StoryPoints,
                    Priority = request.Priority,
                    AssigneeId = request.AssigneeId,
                    Status = string.IsNullOrWhiteSpace(request.Status)
                        ? (string.IsNullOrWhiteSpace(request.SprintId) ? "Backlog" : "SprintBacklog")
                        : request.Status,
                    Key = $"{(project.Key ?? "PROJ").ToUpperInvariant()}-{storyNumber}",
                    CreatedAt = DateTime.UtcNow
                };

                store.Data.UserStories.Add(story);
                store.Save();
                return Results.Created($"/api/stories/{story.Id}", ToStoryDto(story, store));
            }
        });

        group.MapPut("/{id}", (string id, CreateStoryRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var story = store.Data.UserStories.FirstOrDefault(item => item.Id == id);
                if (story is null)
                {
                    return Results.NotFound();
                }

                story.Title = request.Title.Trim();
                story.Description = request.Description?.Trim();
                story.AcceptanceCriteria = request.AcceptanceCriteria?.Trim();
                story.StoryPoints = request.StoryPoints;
                story.Priority = request.Priority;
                story.ProjectId = string.IsNullOrWhiteSpace(request.ProjectId) ? story.ProjectId : request.ProjectId;
                story.SprintId = request.SprintId;
                story.AssigneeId = request.AssigneeId;
                story.Status = string.IsNullOrWhiteSpace(request.Status) ? story.Status : request.Status;
                story.UpdatedAt = DateTime.UtcNow;
                AddStoryHistory(store, story.Id, request.AssigneeId ?? story.AssigneeId ?? story.CreatedById, "StoryUpdated", "Se actualizaron los detalles de la historia.");

                store.Save();
                return Results.Ok(ToStoryDto(story, store));
            }
        });

        group.MapPut("/{id}/status", (string id, UpdateStatusRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var story = store.Data.UserStories.FirstOrDefault(item => item.Id == id);
                if (story is null)
                {
                    return Results.NotFound();
                }

                story.Status = request.Status;
                story.UpdatedAt = DateTime.UtcNow;
                AddStoryHistory(store, story.Id, story.AssigneeId ?? story.CreatedById, "StatusChanged", $"Estado cambiado a {request.Status}.");
                store.Save();
                return Results.Ok(new { message = "Historia actualizada" });
            }
        });

        group.MapPost("/{id}/move-to-sprint", (string id, string sprintId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var story = store.Data.UserStories.FirstOrDefault(item => item.Id == id);
                if (story is null)
                {
                    return Results.NotFound();
                }

                if (store.Data.Sprints.All(sprint => sprint.Id != sprintId))
                {
                    return Results.BadRequest("El sprint no existe");
                }

                story.SprintId = sprintId;
                story.Status = "Backlog";
                story.UpdatedAt = DateTime.UtcNow;
                AddStoryHistory(store, story.Id, story.AssigneeId ?? story.CreatedById, "SprintMove", "Historia movida a sprint.");
                store.Save();

                return Results.Ok(new { message = "Historia movida al sprint" });
            }
        });

        group.MapPost("/{id}/move-to-backlog", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var story = store.Data.UserStories.FirstOrDefault(item => item.Id == id);
                if (story is null)
                {
                    return Results.NotFound();
                }

                story.SprintId = null;
                story.Status = "Backlog";
                story.UpdatedAt = DateTime.UtcNow;
                AddStoryHistory(store, story.Id, story.AssigneeId ?? story.CreatedById, "SprintMove", "Historia movida a backlog.");
                store.Save();

                return Results.Ok(new { message = "Historia movida al backlog" });
            }
        });

        group.MapPost("/{id}/comments", (string id, CreateStoryCommentRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var story = store.Data.UserStories.FirstOrDefault(item => item.Id == id);
                if (story is null)
                {
                    return Results.NotFound();
                }

                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return Results.BadRequest("El comentario no puede estar vacio.");
                }

                var comment = new StoryComment
                {
                    Id = Guid.NewGuid().ToString(),
                    StoryId = id,
                    UserId = request.UserId,
                    Message = request.Message.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                store.Data.StoryComments.Add(comment);
                AddStoryHistory(store, id, request.UserId, "CommentAdded", "Se agrego un comentario.");
                store.Save();

                var userName = store.Data.Users.FirstOrDefault(user => user.Id == comment.UserId)?.Name ?? "Usuario";
                return Results.Ok(new StoryCommentDto
                {
                    Id = comment.Id,
                    StoryId = comment.StoryId,
                    UserId = comment.UserId,
                    Message = comment.Message,
                    CreatedAt = comment.CreatedAt,
                    UserName = userName
                });
            }
        });

        group.MapDelete("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var story = store.Data.UserStories.FirstOrDefault(item => item.Id == id);
                if (story is null)
                {
                    return Results.NotFound();
                }

                store.Data.UserStories.Remove(story);
                store.Data.Tasks.RemoveAll(task => task.StoryId == id);
                store.Data.StoryComments.RemoveAll(comment => comment.StoryId == id);
                store.Data.StoryHistory.RemoveAll(item => item.StoryId == id);
                store.Save();
                return Results.Ok(new { message = "Historia eliminada" });
            }
        });
    }

    private static UserStoryDto ToStoryDto(UserStory story, AppDataStore store)
    {
        var tasks = store.Data.Tasks
            .Where(task => task.StoryId == story.Id)
            .Select(task =>
            {
                var assignedUser = store.Data.Users.FirstOrDefault(user => user.Id == task.AssignedToId);
                return new TaskItemDto
                {
                    Id = task.Id,
                    StoryId = task.StoryId,
                    Title = task.Title,
                    Description = task.Description,
                    EstimatedHours = task.EstimatedHours,
                    ActualHours = task.ActualHours,
                    Status = task.Status,
                    Priority = task.Priority,
                    AssignedToId = task.AssignedToId,
                    AssignedToName = assignedUser?.Name,
                    StoryTitle = story.Title,
                    CreatedAt = task.CreatedAt,
                    CompletedAt = task.CompletedAt
                };
            })
            .ToList();

        return new UserStoryDto
        {
            Id = story.Id,
            ProjectId = story.ProjectId,
            SprintId = story.SprintId,
            Title = story.Title,
            Description = story.Description,
            AcceptanceCriteria = story.AcceptanceCriteria,
            Key = story.Key,
            Status = story.Status,
            Priority = story.Priority,
            StoryPoints = story.StoryPoints,
            Type = story.Type,
            AssigneeId = story.AssigneeId,
            CreatedById = story.CreatedById,
            CreatedAt = story.CreatedAt,
            UpdatedAt = story.UpdatedAt,
            TaskCount = tasks.Count,
            CompletedTaskCount = tasks.Count(task => task.Status == "Done"),
            Tasks = tasks,
            Comments = store.Data.StoryComments
                .Where(comment => comment.StoryId == story.Id)
                .OrderByDescending(comment => comment.CreatedAt)
                .Select(comment => new StoryCommentDto
                {
                    Id = comment.Id,
                    StoryId = comment.StoryId,
                    UserId = comment.UserId,
                    Message = comment.Message,
                    CreatedAt = comment.CreatedAt,
                    UserName = store.Data.Users.FirstOrDefault(user => user.Id == comment.UserId)?.Name ?? "Usuario"
                })
                .ToList(),
            History = store.Data.StoryHistory
                .Where(item => item.StoryId == story.Id)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new StoryHistoryDto
                {
                    Id = item.Id,
                    StoryId = item.StoryId,
                    UserId = item.UserId,
                    EventType = item.EventType,
                    Message = item.Message,
                    CreatedAt = item.CreatedAt,
                    UserName = store.Data.Users.FirstOrDefault(user => user.Id == item.UserId)?.Name ?? "Sistema"
                })
                .ToList()
        };
    }

    private static void AddStoryHistory(AppDataStore store, string storyId, string? userId, string eventType, string message)
    {
        store.Data.StoryHistory.Add(new StoryHistoryEntry
        {
            Id = Guid.NewGuid().ToString(),
            StoryId = storyId,
            UserId = string.IsNullOrWhiteSpace(userId) ? "system" : userId!,
            EventType = eventType,
            Message = message,
            CreatedAt = DateTime.UtcNow
        });
    }
}
