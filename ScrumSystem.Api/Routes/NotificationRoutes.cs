using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class NotificationRoutes
{
    public static void MapNotificationRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications");

        group.MapGet("/", async (string userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            string sql = @"
                SELECT CAST(n.Id AS NVARCHAR(36)) as Id, CAST(n.UserId AS NVARCHAR(36)) as UserId, 
                       CAST(n.ProjectId AS NVARCHAR(36)) as ProjectId, CAST(n.CreatorId AS NVARCHAR(36)) as CreatorId, n.Title, n.Message, n.Type, n.IsRead, n.CreatedAt,
                       p.Name as ProjectName, u.Name as CreatorName,
                       ISNULL(pi.Status, 'pending') as InvitationStatus
                FROM Notifications n
                LEFT JOIN Projects p ON n.ProjectId = p.Id
                LEFT JOIN Users u ON n.CreatorId = u.Id
                LEFT JOIN ProjectInvitations pi ON n.ProjectId = pi.ProjectId AND n.UserId = pi.UserId AND n.Type = 'project_invitation'
                WHERE CAST(n.UserId AS NVARCHAR(36)) = @UserId
                ORDER BY n.CreatedAt DESC";

            var notifications = new List<NotificationDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    notifications.Add(new NotificationDto
                    {
                        Id = reader.GetString(0),
                        UserId = reader.GetString(1),
                        ProjectId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CreatorId = reader.GetString(3),
                        Title = reader.GetString(4),
                        Message = reader.GetString(5),
                        Type = reader.GetString(6),
                        IsRead = reader.GetBoolean(7),
                        CreatedAt = reader.GetDateTime(8),
                        ProjectName = reader.IsDBNull(9) ? null : reader.GetString(9),
                        CreatorName = reader.IsDBNull(10) ? null : reader.GetString(10),
                        Status = reader.IsDBNull(11) ? null : reader.GetString(11)
                    });
                }
            }

            return Results.Ok(notifications);
        });

        group.MapGet("/unread-count", async (string userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Notifications WHERE CAST(UserId AS NVARCHAR(36)) = @UserId AND IsRead = 0", connection))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                var count = await cmd.ExecuteScalarAsync();
                return Results.Ok(new { count = count ?? 0 });
            }
        });

        group.MapPut("/{id}/read", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using (var cmd = new SqlCommand("UPDATE Notifications SET IsRead = 1 WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                
                if (rowsAffected == 0)
                {
                    return Results.NotFound();
                }
            }

            return Results.Ok(new { message = "Notificación marcada como leída" });
        });

        group.MapPut("/read-all", async (string userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using (var cmd = new SqlCommand("UPDATE Notifications SET IsRead = 1 WHERE CAST(UserId AS NVARCHAR(36)) = @UserId", connection))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Todas las notificaciones marcadas como leídas" });
        });

        group.MapPost("/{id}/accept", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Obtener información de la notificación (invitación)
                string notificationType = "", projectId = "", userId = "", invitedById = "", projectName = "";
                using (var notifCmd = new SqlCommand(@"
                    SELECT Type, CAST(ProjectId AS NVARCHAR(36)) as ProjectId, CAST(UserId AS NVARCHAR(36)) as UserId, 
                           CAST(CreatorId AS NVARCHAR(36)) as CreatorId
                    FROM Notifications 
                    WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection, transaction))
                {
                    notifCmd.Parameters.AddWithValue("@Id", id);
                    using var reader = await notifCmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        reader.Close();
                        transaction.Rollback();
                        return Results.NotFound(new { message = "Notificación no encontrada" });
                    }
                    notificationType = reader.GetString(0);
                    projectId = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    userId = reader.GetString(2);
                    invitedById = reader.GetString(3);
                    reader.Close();
                }

                // Solo procesar si es invitación de proyecto
                if (notificationType == "project_invitation" && !string.IsNullOrEmpty(projectId))
                {
                    // Obtener nombre del proyecto
                    using (var projCmd = new SqlCommand("SELECT Name FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @ProjectId", connection, transaction))
                    {
                        projCmd.Parameters.AddWithValue("@ProjectId", projectId);
                        var result = await projCmd.ExecuteScalarAsync();
                        projectName = result?.ToString() ?? "";
                    }

                    // Buscar y actualizar la invitación correspondiente
                    using (var updateInvCmd = new SqlCommand(@"
                        UPDATE ProjectInvitations 
                        SET Status = 'accepted', RespondedAt = @RespondedAt
                        WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId AND CAST(UserId AS NVARCHAR(36)) = @UserId AND Status = 'pending'", connection, transaction))
                    {
                        updateInvCmd.Parameters.AddWithValue("@ProjectId", projectId);
                        updateInvCmd.Parameters.AddWithValue("@UserId", userId);
                        updateInvCmd.Parameters.AddWithValue("@RespondedAt", DateTime.UtcNow);
                        await updateInvCmd.ExecuteNonQueryAsync();
                    }

                    // Agregar como miembro
                    using (var memberCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT 1 FROM ProjectMembers WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId AND CAST(UserId AS NVARCHAR(36)) = @UserId)
                        INSERT INTO ProjectMembers (Id, ProjectId, UserId, Role, JoinedAt) 
                        VALUES (@Id, @ProjectId, @UserId, @Role, @JoinedAt)", connection, transaction))
                    {
                        memberCmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
                        memberCmd.Parameters.AddWithValue("@ProjectId", Guid.Parse(projectId));
                        memberCmd.Parameters.AddWithValue("@UserId", Guid.Parse(userId));
                        memberCmd.Parameters.AddWithValue("@Role", "Developer");
                        memberCmd.Parameters.AddWithValue("@JoinedAt", DateTime.UtcNow);
                        await memberCmd.ExecuteNonQueryAsync();
                    }

                    // Notificar al creador
                    using (var notifCreatorCmd = new SqlCommand(@"
                        INSERT INTO Notifications (Id, UserId, ProjectId, CreatorId, Title, Message, Type, IsRead, CreatedAt)
                        VALUES (@Id, @UserId, @ProjectId, @CreatorId, @Title, @Message, @Type, 0, @CreatedAt)", connection, transaction))
                    {
                        notifCreatorCmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
                        notifCreatorCmd.Parameters.AddWithValue("@UserId", Guid.Parse(invitedById));
                        notifCreatorCmd.Parameters.AddWithValue("@ProjectId", Guid.Parse(projectId));
                        notifCreatorCmd.Parameters.AddWithValue("@CreatorId", Guid.Parse(userId));
                        notifCreatorCmd.Parameters.AddWithValue("@Title", "Invitación aceptada");
                        notifCreatorCmd.Parameters.AddWithValue("@Message", $"Un usuario ha aceptado unirse al proyecto '{projectName}'.");
                        notifCreatorCmd.Parameters.AddWithValue("@Type", "project_invitation_accepted");
                        notifCreatorCmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
                        await notifCreatorCmd.ExecuteNonQueryAsync();
                    }
                }

                // Marcar notificación como leída
                using (var cmd = new SqlCommand("UPDATE Notifications SET IsRead = 1 WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    await cmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                return Results.Ok(new { message = "Invitación aceptada. Ahora eres miembro del proyecto." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Results.Problem($"Error al aceptar invitación: {ex.Message}");
            }
        });

        group.MapPost("/{id}/reject", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using (var deleteCmd = new SqlCommand("DELETE FROM Notifications WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                deleteCmd.Parameters.AddWithValue("@Id", id);
                var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();
                
                if (rowsAffected == 0)
                {
                    return Results.NotFound();
                }
            }

            return Results.Ok(new { message = "Notificación rechazada y eliminada" });
        });

        group.MapDelete("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using (var deleteCmd = new SqlCommand("DELETE FROM Notifications WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                deleteCmd.Parameters.AddWithValue("@Id", id);
                var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();
                
                if (rowsAffected == 0)
                {
                    return Results.NotFound();
                }
            }

            return Results.Ok(new { message = "Notificación eliminada" });
        });

        group.MapPost("/", async (CreateNotificationRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var notificationId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;

            using (var insertCmd = new SqlCommand(@"
                INSERT INTO Notifications (Id, UserId, ProjectId, CreatorId, Title, Message, Type, IsRead, CreatedAt) 
                VALUES (@Id, @UserId, @ProjectId, @CreatorId, @Title, @Message, @Type, @IsRead, @CreatedAt)", connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", notificationId);
                insertCmd.Parameters.AddWithValue("@UserId", Guid.Parse(request.UserId));
                insertCmd.Parameters.AddWithValue("@ProjectId", string.IsNullOrWhiteSpace(request.ProjectId) ? DBNull.Value : Guid.Parse(request.ProjectId));
                insertCmd.Parameters.AddWithValue("@CreatorId", Guid.Parse(request.CreatorId));
                insertCmd.Parameters.AddWithValue("@Title", request.Title.Trim());
                insertCmd.Parameters.AddWithValue("@Message", request.Message.Trim());
                insertCmd.Parameters.AddWithValue("@Type", request.Type);
                insertCmd.Parameters.AddWithValue("@IsRead", false);
                insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync();
            }

            return Results.Created($"/api/notifications/{notificationId}", new { id = notificationId.ToString(), message = "Notificación creada exitosamente" });
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
