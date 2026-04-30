using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class NotificationRoutes
{
    public static void MapNotificationRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications");

        // Get user notifications
        group.MapGet("/", async (Guid userId, DatabaseContext db) =>
        {
            var notifications = new List<NotificationDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT n.Id, n.UserId, n.Type, n.Title, n.Message, n.ProjectId, n.Status, n.IsRead, n.CreatedAt,
                       p.Name as ProjectName,
                       u.Name as CreatorName
                FROM Notifications n
                LEFT JOIN Projects p ON n.ProjectId = p.Id
                LEFT JOIN Users u ON p.ProductOwnerId = u.Id
                WHERE n.UserId = @UserId
                ORDER BY n.CreatedAt DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                notifications.Add(new NotificationDto
                {
                    Id = (Guid)reader["Id"],
                    UserId = (Guid)reader["UserId"],
                    Type = reader["Type"].ToString()!,
                    Title = reader["Title"].ToString()!,
                    Message = reader["Message"].ToString()!,
                    ProjectId = reader["ProjectId"] as Guid?,
                    ProjectName = reader["ProjectName"]?.ToString(),
                    CreatorName = reader["CreatorName"]?.ToString(),
                    Status = reader["Status"]?.ToString() ?? "pending",
                    IsRead = (bool)reader["IsRead"],
                    CreatedAt = (DateTime)reader["CreatedAt"]
                });
            }

            return Results.Ok(notifications);
        });

        // Get unread count
        group.MapGet("/unread-count", async (Guid userId, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = "SELECT COUNT(*) FROM Notifications WHERE UserId = @UserId AND IsRead = 0";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);

            var count = (int)await cmd.ExecuteScalarAsync();

            return Results.Ok(new { count });
        });

        // Mark as read
        group.MapPut("/{id:guid}/read", async (Guid id, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = "UPDATE Notifications SET IsRead = 1 WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                return Results.NotFound();
            }

            return Results.Ok(new { message = "Notificación marcada como leída" });
        });

        // Mark all as read
        group.MapPut("/read-all", async (Guid userId, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = "UPDATE Notifications SET IsRead = 1 WHERE UserId = @UserId AND IsRead = 0";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);

            await cmd.ExecuteNonQueryAsync();

            return Results.Ok(new { message = "Todas las notificaciones marcadas como leídas" });
        });

        // Accept invitation
        group.MapPost("/{id:guid}/accept", async (Guid id, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Get notification details
                var notifSql = "SELECT ProjectId, UserId FROM Notifications WHERE Id = @Id";
                using var notifCmd = new SqlCommand(notifSql, conn);
                notifCmd.Parameters.AddWithValue("@Id", id);
                using var reader = await notifCmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Results.NotFound();
                }

                var projectId = reader["ProjectId"] as Guid?;
                var userId = (Guid)reader["UserId"];
                reader.Close();

                if (!projectId.HasValue)
                {
                    return Results.Problem("Esta notificación no está asociada a un proyecto", statusCode: 400);
                }

                // Check if already a member
                var checkSql = "SELECT COUNT(*) FROM ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @UserId";
                using var checkCmd = new SqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@ProjectId", projectId.Value);
                checkCmd.Parameters.AddWithValue("@UserId", userId);
                var count = (int)await checkCmd.ExecuteScalarAsync();

                if (count == 0)
                {
                    // Add to project members
                    var addSql = "INSERT INTO ProjectMembers (ProjectId, UserId) VALUES (@ProjectId, @UserId)";
                    using var addCmd = new SqlCommand(addSql, conn);
                    addCmd.Parameters.AddWithValue("@ProjectId", projectId.Value);
                    addCmd.Parameters.AddWithValue("@UserId", userId);
                    await addCmd.ExecuteNonQueryAsync();
                }

                // Update notification status
                var updateSql = "UPDATE Notifications SET Status = 'accepted', IsRead = 1 WHERE Id = @Id";
                using var updateCmd = new SqlCommand(updateSql, conn);
                updateCmd.Parameters.AddWithValue("@Id", id);
                await updateCmd.ExecuteNonQueryAsync();

                return Results.Ok(new { message = "Invitación aceptada" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error al aceptar invitación: {ex.Message}");
            }
        });

        // Reject invitation
        group.MapPost("/{id:guid}/reject", async (Guid id, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Remove from project members if exists
                var notifSql = "SELECT ProjectId, UserId FROM Notifications WHERE Id = @Id";
                using var notifCmd = new SqlCommand(notifSql, conn);
                notifCmd.Parameters.AddWithValue("@Id", id);
                using var reader = await notifCmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Results.NotFound();
                }

                var projectId = reader["ProjectId"] as Guid?;
                var userId = (Guid)reader["UserId"];
                reader.Close();

                if (projectId.HasValue)
                {
                    var deleteSql = "DELETE FROM ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @UserId";
                    using var deleteCmd = new SqlCommand(deleteSql, conn);
                    deleteCmd.Parameters.AddWithValue("@ProjectId", projectId.Value);
                    deleteCmd.Parameters.AddWithValue("@UserId", userId);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                // Update notification status
                var updateSql = "UPDATE Notifications SET Status = 'rejected', IsRead = 1 WHERE Id = @Id";
                using var updateCmd = new SqlCommand(updateSql, conn);
                updateCmd.Parameters.AddWithValue("@Id", id);
                await updateCmd.ExecuteNonQueryAsync();

                return Results.Ok(new { message = "Invitación rechazada" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error al rechazar invitación: {ex.Message}");
            }
        });

        // Delete notification
        group.MapDelete("/{id:guid}", async (Guid id, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = "DELETE FROM Notifications WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                return Results.NotFound();
            }

            return Results.Ok(new { message = "Notificación eliminada" });
        });

        // Create notification (internal use)
        group.MapPost("/", async (CreateNotificationRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var id = Guid.NewGuid();

                var sql = @"
                    INSERT INTO Notifications (Id, UserId, Type, Title, Message, ProjectId, IsRead, CreatedAt)
                    VALUES (@Id, @UserId, @Type, @Title, @Message, @ProjectId, 0, GETUTCDATE())";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@UserId", request.UserId);
                cmd.Parameters.AddWithValue("@Type", request.Type);
                cmd.Parameters.AddWithValue("@Title", request.Title);
                cmd.Parameters.AddWithValue("@Message", request.Message);
                cmd.Parameters.AddWithValue("@ProjectId", request.ProjectId.HasValue ? (object)request.ProjectId.Value : DBNull.Value);

                await cmd.ExecuteNonQueryAsync();

                return Results.Created($"/api/notifications/{id}", new Notification
                {
                    Id = id,
                    UserId = request.UserId,
                    Type = request.Type,
                    Title = request.Title,
                    Message = request.Message,
                    ProjectId = request.ProjectId,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating notification: {ex.Message}");
            }
        });
    }
}

public class CreateNotificationRequest
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public Guid? ProjectId { get; set; }
}
