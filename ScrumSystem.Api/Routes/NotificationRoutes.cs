using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class NotificationRoutes
{
    public static void MapNotificationRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications");

        group.MapGet("/", (string userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var notifications = store.Data.Notifications
                    .Where(notification => notification.UserId == userId)
                    .OrderByDescending(notification => notification.CreatedAt)
                    .Select(notification => ToNotificationDto(notification, store))
                    .ToList();

                return Results.Ok(notifications);
            }
        });

        group.MapGet("/unread-count", (string userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var count = store.Data.Notifications.Count(notification => notification.UserId == userId && !notification.IsRead);
                return Results.Ok(new { count });
            }
        });

        group.MapPut("/{id}/read", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var notification = store.Data.Notifications.FirstOrDefault(item => item.Id == id);
                if (notification is null)
                {
                    return Results.NotFound();
                }

                notification.IsRead = true;
                store.Save();
                return Results.Ok(new { message = "Notificación marcada como leída" });
            }
        });

        group.MapPut("/read-all", (string userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                foreach (var notification in store.Data.Notifications.Where(item => item.UserId == userId))
                {
                    notification.IsRead = true;
                }

                store.Save();
                return Results.Ok(new { message = "Todas las notificaciones marcadas como leídas" });
            }
        });

        group.MapPost("/{id}/accept", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var notification = store.Data.Notifications.FirstOrDefault(item => item.Id == id);
                if (notification is null)
                {
                    return Results.NotFound();
                }

                if (!string.IsNullOrWhiteSpace(notification.ProjectId) &&
                    store.Data.ProjectMembers.All(member => member.ProjectId != notification.ProjectId || member.UserId != notification.UserId))
                {
                    store.Data.ProjectMembers.Add(new ProjectMember
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProjectId = notification.ProjectId!,
                        UserId = notification.UserId,
                        Role = "Developer",
                        JoinedAt = DateTime.UtcNow
                    });
                }

                notification.Status = "accepted";
                notification.IsRead = true;
                store.Save();
                return Results.Ok(new { message = "Invitación aceptada" });
            }
        });

        group.MapPost("/{id}/reject", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var notification = store.Data.Notifications.FirstOrDefault(item => item.Id == id);
                if (notification is null)
                {
                    return Results.NotFound();
                }

                notification.Status = "rejected";
                notification.IsRead = true;
                store.Save();
                return Results.Ok(new { message = "Invitación rechazada" });
            }
        });

        group.MapDelete("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var notification = store.Data.Notifications.FirstOrDefault(item => item.Id == id);
                if (notification is null)
                {
                    return Results.NotFound();
                }

                store.Data.Notifications.Remove(notification);
                store.Save();
                return Results.Ok(new { message = "Notificación eliminada" });
            }
        });

        group.MapPost("/", (CreateNotificationRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var notification = new Notification
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = request.UserId,
                    Type = request.Type,
                    Title = request.Title,
                    Message = request.Message,
                    ProjectId = request.ProjectId,
                    CreatorId = request.CreatorId,
                    IsRead = false,
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow
                };

                store.Data.Notifications.Add(notification);
                store.Save();
                return Results.Created($"/api/notifications/{notification.Id}", notification);
            }
        });
    }

    private static NotificationDto ToNotificationDto(Notification notification, AppDataStore store)
    {
        var project = store.Data.Projects.FirstOrDefault(item => item.Id == notification.ProjectId);
        var creator = store.Data.Users.FirstOrDefault(item => item.Id == notification.CreatorId);

        return new NotificationDto
        {
            Id = notification.Id,
            UserId = notification.UserId,
            Type = notification.Type,
            Title = notification.Title,
            Message = notification.Message,
            ProjectId = notification.ProjectId,
            CreatorId = notification.CreatorId,
            Status = notification.Status,
            IsRead = notification.IsRead,
            CreatedAt = notification.CreatedAt,
            ProjectName = project?.Name,
            CreatorName = creator?.Name
        };
    }
}
