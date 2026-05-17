using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class ProjectRoutes
{
    public static void MapProjectRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects");

        group.MapGet("/", async (string? userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = string.IsNullOrWhiteSpace(userId)
                ? "SELECT CAST(Id AS NVARCHAR(36)), Name, Description, [Key], Color, Icon, CAST(CreatorId AS NVARCHAR(36)), CreatedAt FROM Projects ORDER BY CreatedAt DESC"
                : @"SELECT CAST(p.Id AS NVARCHAR(36)), p.Name, p.Description, p.[Key], p.Color, p.Icon, CAST(p.CreatorId AS NVARCHAR(36)), p.CreatedAt 
                     FROM Projects p
                     INNER JOIN ProjectMembers pm ON p.Id = pm.ProjectId
                     WHERE CAST(pm.UserId AS NVARCHAR(36)) = @UserId
                     ORDER BY p.CreatedAt DESC";

            using var cmd = new SqlCommand(sql, connection);
            if (!string.IsNullOrWhiteSpace(userId))
                cmd.Parameters.AddWithValue("@UserId", userId);

            var projects = new List<ProjectDto>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                projects.Add(new ProjectDto
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Key = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Color = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Icon = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatorId = reader.GetString(6),
                    CreatedAt = reader.GetDateTime(7)
                });
            }

            return Results.Ok(projects);
        });

        group.MapGet("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(Id AS NVARCHAR(36)) as Id, Name, Description, [Key], Color, Icon, 
                       CAST(CreatorId AS NVARCHAR(36)) as CreatorId, CreatedAt
                FROM Projects 
                WHERE Id = @Id";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Id", id);
            
            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.NotFound();
            }

            var project = new
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Key = reader.IsDBNull(3) ? null : reader.GetString(3),
                Color = reader.IsDBNull(4) ? null : reader.GetString(4),
                Icon = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatorId = reader.GetString(6),
                CreatedAt = reader.GetDateTime(7)
            };

            return Results.Ok(project);
        });

        group.MapPost("/", async (CreateProjectRequest request, DatabaseContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.CreatedById))
            {
                return Results.BadRequest("El proyecto requiere un creador válido");
            }

            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verify creator exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE CAST(Id AS NVARCHAR(36)) = @UserId", connection))
            {
                checkCmd.Parameters.AddWithValue("@UserId", request.CreatedById);
                var count = (int)await checkCmd.ExecuteScalarAsync();
                if (count == 0)
                {
                    return Results.BadRequest("El usuario creador no existe");
                }
            }

            var projectId = Guid.NewGuid();
            var projectKey = string.IsNullOrWhiteSpace(request.Key) ? BuildProjectKey(request.Name) : request.Key.Trim().ToUpperInvariant();
            var createdAt = DateTime.UtcNow;

            // Insert project
            var insertSql = @"
                INSERT INTO Projects (Id, Name, Description, [Key], Color, Icon, CreatorId, CreatedAt) 
                VALUES (@Id, @Name, @Description, @Key, @Color, @Icon, @CreatorId, @CreatedAt)";
            
            using (var insertCmd = new SqlCommand(insertSql, connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", projectId);
                insertCmd.Parameters.AddWithValue("@Name", request.Name.Trim());
                insertCmd.Parameters.AddWithValue("@Description", (object?)request.Description?.Trim() ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@Key", (object?)projectKey ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@Color", (object?)request.Color ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@Icon", (object?)request.Icon ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@CreatorId", Guid.Parse(request.CreatedById));
                insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync();
            }

            // Add creator as Owner
            var addMemberSql = "INSERT INTO ProjectMembers (Id, ProjectId, UserId, Role, JoinedAt) VALUES (@Id, @ProjectId, @UserId, @Role, @JoinedAt)";
            using (var memberCmd = new SqlCommand(addMemberSql, connection))
            {
                memberCmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
                memberCmd.Parameters.AddWithValue("@ProjectId", projectId);
                memberCmd.Parameters.AddWithValue("@UserId", Guid.Parse(request.CreatedById));
                memberCmd.Parameters.AddWithValue("@Role", "Product Owner");
                memberCmd.Parameters.AddWithValue("@JoinedAt", createdAt);
                await memberCmd.ExecuteNonQueryAsync();
            }

            var projectDto = new ProjectDto
            {
                Id = projectId.ToString(),
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                Key = projectKey,
                Color = request.Color,
                Icon = request.Icon,
                CreatorId = request.CreatedById,
                CreatedAt = createdAt
            };

            return Results.Created($"/api/projects/{projectId}", projectDto);
        });

        // Obtener miembros de un proyecto
        group.MapGet("/{id}/members", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verificar que el proyecto existe
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Projects WHERE Id = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var count = (int)await checkCmd.ExecuteScalarAsync();
                if (count == 0)
                {
                    return Results.NotFound(new { message = "Proyecto no encontrado" });
                }
            }

            // Obtener miembros del proyecto
            var sql = @"
                SELECT CAST(pm.UserId AS NVARCHAR(36)) as Id, u.Name, u.Email, pm.Role, pm.JoinedAt,
                       CAST(CASE WHEN p.CreatorId = pm.UserId THEN 1 ELSE 0 END AS BIT) as IsCreator
                FROM ProjectMembers pm
                INNER JOIN Users u ON pm.UserId = u.Id
                INNER JOIN Projects p ON pm.ProjectId = p.Id
                WHERE CAST(pm.ProjectId AS NVARCHAR(36)) = @ProjectId
                ORDER BY u.Name";

            var members = new List<object>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@ProjectId", id);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    members.Add(new
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Email = reader.GetString(2),
                        Role = reader.GetString(3),
                        JoinedAt = reader.GetDateTime(4),
                        IsCreator = reader.GetBoolean(5)
                    });
                }
            }

            return Results.Ok(members);
        });

        group.MapPost("/{id}/members", async (string id, AddProjectMemberRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verificar que el proyecto existe
            string projectName = "";
            string creatorId = "";
            using (var projectCmd = new SqlCommand("SELECT Name, CAST(CreatorId AS NVARCHAR(36)) FROM Projects WHERE Id = @Id", connection))
            {
                projectCmd.Parameters.AddWithValue("@Id", id);
                using var reader = await projectCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return Results.NotFound();
                }
                projectName = reader.GetString(0);
                creatorId = reader.GetString(1);
            }

            // Verificar que el usuario existe
            using (var userCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE CAST(Id AS NVARCHAR(36)) = @UserId", connection))
            {
                userCmd.Parameters.AddWithValue("@UserId", request.UserId);
                var count = await userCmd.ExecuteScalarAsync();
                if (count == null || (int)count == 0)
                {
                    return Results.NotFound();
                }
            }

            // Verificar si ya es miembro
            using (var memberCmd = new SqlCommand("SELECT COUNT(*) FROM ProjectMembers WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId AND CAST(UserId AS NVARCHAR(36)) = @UserId", connection))
            {
                memberCmd.Parameters.AddWithValue("@ProjectId", id);
                memberCmd.Parameters.AddWithValue("@UserId", request.UserId);
                var count = await memberCmd.ExecuteScalarAsync();
                if (count != null && (int)count > 0)
                {
                    return Results.Ok(new { message = "El usuario ya pertenece al proyecto" });
                }
            }

            // Verificar si ya tiene una invitación pendiente
            using (var invCmd = new SqlCommand("SELECT COUNT(*) FROM ProjectInvitations WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId AND CAST(UserId AS NVARCHAR(36)) = @UserId AND Status = 'pending'", connection))
            {
                invCmd.Parameters.AddWithValue("@ProjectId", id);
                invCmd.Parameters.AddWithValue("@UserId", request.UserId);
                var count = await invCmd.ExecuteScalarAsync();
                if (count != null && (int)count > 0)
                {
                    return Results.Ok(new { message = "Ya existe una invitación pendiente para este usuario" });
                }
            }

            // Crear invitación pendiente
            var memberRole = NormalizeProjectRole(request.Role);
            var invitationId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;
            using (var insertCmd = new SqlCommand(@"
                INSERT INTO ProjectInvitations (Id, ProjectId, UserId, InvitedById, Role, Status, CreatedAt)
                VALUES (@Id, @ProjectId, @UserId, @InvitedById, @Role, @Status, @CreatedAt)", connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", invitationId);
                insertCmd.Parameters.AddWithValue("@ProjectId", Guid.Parse(id));
                insertCmd.Parameters.AddWithValue("@UserId", Guid.Parse(request.UserId));
                insertCmd.Parameters.AddWithValue("@InvitedById", Guid.Parse(creatorId));
                insertCmd.Parameters.AddWithValue("@Role", memberRole);
                insertCmd.Parameters.AddWithValue("@Status", "pending");
                insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync();
            }

            // Crear notificación para el usuario invitado
            using (var notifCmd = new SqlCommand(@"
                INSERT INTO Notifications (Id, UserId, ProjectId, CreatorId, Title, Message, Type, IsRead, CreatedAt)
                VALUES (@Id, @UserId, @ProjectId, @CreatorId, @Title, @Message, @Type, 0, @CreatedAt)", connection))
            {
                notifCmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
                notifCmd.Parameters.AddWithValue("@UserId", Guid.Parse(request.UserId));
                notifCmd.Parameters.AddWithValue("@ProjectId", Guid.Parse(id));
                notifCmd.Parameters.AddWithValue("@CreatorId", Guid.Parse(creatorId));
                notifCmd.Parameters.AddWithValue("@Title", "Invitación a proyecto");
                notifCmd.Parameters.AddWithValue("@Message", $"Has sido invitado a unirte al proyecto '{projectName}'.");
                notifCmd.Parameters.AddWithValue("@Type", "project_invitation");
                notifCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await notifCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Invitación enviada correctamente", invitationId = invitationId.ToString() });
        });

        // Aceptar invitación
        group.MapPut("/{id}/members/{memberId}", async (string id, string memberId, UpdateProjectMemberRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var role = NormalizeProjectRole(request.Role);
            using (var projectCmd = new SqlCommand("SELECT CAST(CreatorId AS NVARCHAR(36)) FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @ProjectId", connection))
            {
                projectCmd.Parameters.AddWithValue("@ProjectId", id);
                var creatorId = (await projectCmd.ExecuteScalarAsync())?.ToString();
                if (creatorId is null)
                {
                    return Results.NotFound(new { message = "Proyecto no encontrado" });
                }

                if (string.Equals(creatorId, memberId, StringComparison.OrdinalIgnoreCase) && role != "Product Owner")
                {
                    return Results.BadRequest(new { message = "El creador debe conservar el rol Product Owner" });
                }
            }

            using var cmd = new SqlCommand(@"
                UPDATE ProjectMembers
                SET Role = @Role
                WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId
                  AND CAST(UserId AS NVARCHAR(36)) = @MemberId", connection);
            cmd.Parameters.AddWithValue("@ProjectId", id);
            cmd.Parameters.AddWithValue("@MemberId", memberId);
            cmd.Parameters.AddWithValue("@Role", role);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            return rowsAffected == 0
                ? Results.NotFound(new { message = "Miembro no encontrado en el proyecto" })
                : Results.Ok(new { message = "Rol del miembro actualizado", role });
        });

        group.MapDelete("/{id}/members/{memberId}", async (string id, string memberId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using (var projectCmd = new SqlCommand("SELECT CAST(CreatorId AS NVARCHAR(36)) FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @ProjectId", connection))
            {
                projectCmd.Parameters.AddWithValue("@ProjectId", id);
                var creatorId = (await projectCmd.ExecuteScalarAsync())?.ToString();
                if (creatorId is null)
                {
                    return Results.NotFound(new { message = "Proyecto no encontrado" });
                }

                if (string.Equals(creatorId, memberId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { message = "No puedes eliminar al creador del proyecto" });
                }
            }

            using var deleteCmd = new SqlCommand(@"
                DELETE FROM ProjectMembers
                WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId
                  AND CAST(UserId AS NVARCHAR(36)) = @MemberId", connection);
            deleteCmd.Parameters.AddWithValue("@ProjectId", id);
            deleteCmd.Parameters.AddWithValue("@MemberId", memberId);

            var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();
            return rowsAffected == 0
                ? Results.NotFound(new { message = "Miembro no encontrado en el proyecto" })
                : Results.Ok(new { message = "Miembro eliminado del proyecto" });
        });

        group.MapPost("/invitations/{invitationId}/accept", async (string invitationId, string userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Verificar invitación - leer datos primero
                string projectId = "", invitedById = "", projectName = "", status = "", invitationRole = "Developer";
                using (var checkCmd = new SqlCommand(@"
                    SELECT CAST(ProjectId AS NVARCHAR(36)), CAST(InvitedById AS NVARCHAR(36)), Status, Role
                    FROM ProjectInvitations 
                    WHERE CAST(Id AS NVARCHAR(36)) = @InvitationId AND CAST(UserId AS NVARCHAR(36)) = @UserId", connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                    checkCmd.Parameters.AddWithValue("@UserId", userId);
                    using var reader = await checkCmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        reader.Close();
                        transaction.Rollback();
                        return Results.NotFound(new { message = "Invitación no encontrada" });
                    }
                    projectId = reader.GetString(0);
                    invitedById = reader.GetString(1);
                    status = reader.GetString(2);
                    invitationRole = reader.IsDBNull(3) ? "Developer" : NormalizeProjectRole(reader.GetString(3));
                    reader.Close();
                }

                if (status != "pending")
                {
                    transaction.Rollback();
                    return Results.BadRequest(new { message = "La invitación ya fue respondida" });
                }

                // Obtener nombre del proyecto
                using (var projCmd = new SqlCommand("SELECT Name FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @ProjectId", connection, transaction))
                {
                    projCmd.Parameters.AddWithValue("@ProjectId", projectId);
                    var result = await projCmd.ExecuteScalarAsync();
                    if (result == null)
                    {
                        transaction.Rollback();
                        return Results.NotFound(new { message = "Proyecto no encontrado" });
                    }
                    projectName = result.ToString()!;
                }

                // Actualizar invitación
                using (var updateInvCmd = new SqlCommand(@"
                    UPDATE ProjectInvitations 
                    SET Status = 'accepted', RespondedAt = @RespondedAt
                    WHERE CAST(Id AS NVARCHAR(36)) = @InvitationId", connection, transaction))
                {
                    updateInvCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                    updateInvCmd.Parameters.AddWithValue("@RespondedAt", DateTime.UtcNow);
                    var rows = await updateInvCmd.ExecuteNonQueryAsync();
                    if (rows == 0)
                    {
                        transaction.Rollback();
                        return Results.Problem("No se pudo actualizar la invitación");
                    }
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
                    memberCmd.Parameters.AddWithValue("@Role", invitationRole);
                    memberCmd.Parameters.AddWithValue("@JoinedAt", DateTime.UtcNow);
                    await memberCmd.ExecuteNonQueryAsync();
                }

                // Crear notificación al creador
                using (var notifCmd = new SqlCommand(@"
                    INSERT INTO Notifications (Id, UserId, ProjectId, CreatorId, Title, Message, Type, IsRead, CreatedAt)
                    VALUES (@Id, @UserId, @ProjectId, @CreatorId, @Title, @Message, @Type, 0, @CreatedAt)", connection, transaction))
                {
                    notifCmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
                    notifCmd.Parameters.AddWithValue("@UserId", Guid.Parse(invitedById));
                    notifCmd.Parameters.AddWithValue("@ProjectId", Guid.Parse(projectId));
                    notifCmd.Parameters.AddWithValue("@CreatorId", Guid.Parse(userId));
                    notifCmd.Parameters.AddWithValue("@Title", "Invitación aceptada");
                    notifCmd.Parameters.AddWithValue("@Message", $"El usuario ha aceptado unirse al proyecto '{projectName}'.");
                    notifCmd.Parameters.AddWithValue("@Type", "project_invitation_accepted");
                    notifCmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
                    await notifCmd.ExecuteNonQueryAsync();
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

        // Rechazar invitación
        group.MapPost("/invitations/{invitationId}/reject", async (string invitationId, string userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Verificar invitación - leer datos primero
                string projectId = "", invitedById = "", projectName = "", status = "";
                using (var checkCmd = new SqlCommand(@"
                    SELECT CAST(ProjectId AS NVARCHAR(36)), CAST(InvitedById AS NVARCHAR(36)), Status
                    FROM ProjectInvitations 
                    WHERE CAST(Id AS NVARCHAR(36)) = @InvitationId AND CAST(UserId AS NVARCHAR(36)) = @UserId", connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                    checkCmd.Parameters.AddWithValue("@UserId", userId);
                    using var reader = await checkCmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        reader.Close();
                        transaction.Rollback();
                        return Results.NotFound(new { message = "Invitación no encontrada" });
                    }
                    projectId = reader.GetString(0);
                    invitedById = reader.GetString(1);
                    status = reader.GetString(2);
                    reader.Close();
                }

                if (status != "pending")
                {
                    transaction.Rollback();
                    return Results.BadRequest(new { message = "La invitación ya fue respondida" });
                }

                // Obtener nombre del proyecto
                using (var projCmd = new SqlCommand("SELECT Name FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @ProjectId", connection, transaction))
                {
                    projCmd.Parameters.AddWithValue("@ProjectId", projectId);
                    var result = await projCmd.ExecuteScalarAsync();
                    projectName = result?.ToString() ?? "desconocido";
                }

                // Actualizar invitación
                using (var updateCmd = new SqlCommand(@"
                    UPDATE ProjectInvitations 
                    SET Status = 'rejected', RespondedAt = @RespondedAt
                    WHERE CAST(Id AS NVARCHAR(36)) = @InvitationId", connection, transaction))
                {
                    updateCmd.Parameters.AddWithValue("@InvitationId", invitationId);
                    updateCmd.Parameters.AddWithValue("@RespondedAt", DateTime.UtcNow);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                // Crear notificación al creador
                using (var notifCmd = new SqlCommand(@"
                    INSERT INTO Notifications (Id, UserId, ProjectId, CreatorId, Title, Message, Type, IsRead, CreatedAt)
                    VALUES (@Id, @UserId, @ProjectId, @CreatorId, @Title, @Message, @Type, 0, @CreatedAt)", connection, transaction))
                {
                    notifCmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
                    notifCmd.Parameters.AddWithValue("@UserId", Guid.Parse(invitedById));
                    notifCmd.Parameters.AddWithValue("@ProjectId", Guid.Parse(projectId));
                    notifCmd.Parameters.AddWithValue("@CreatorId", Guid.Parse(userId));
                    notifCmd.Parameters.AddWithValue("@Title", "Invitación rechazada");
                    notifCmd.Parameters.AddWithValue("@Message", $"El usuario ha rechazado la invitación al proyecto '{projectName}'.");
                    notifCmd.Parameters.AddWithValue("@Type", "project_invitation_rejected");
                    notifCmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
                    await notifCmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                return Results.Ok(new { message = "Invitación rechazada" });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Results.Problem($"Error al rechazar invitación: {ex.Message}");
            }
        });

        // Listar invitaciones pendientes del usuario
        group.MapGet("/invitations/pending", async (string userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(i.Id AS NVARCHAR(36)), CAST(i.ProjectId AS NVARCHAR(36)) as ProjectId, 
                       CAST(i.UserId AS NVARCHAR(36)) as UserId, CAST(i.InvitedById AS NVARCHAR(36)) as InvitedById,
                       i.Role, i.Status, i.CreatedAt, p.Name as ProjectName, p.[Key] as ProjectKey
                FROM ProjectInvitations i
                INNER JOIN Projects p ON i.ProjectId = p.Id
                WHERE CAST(i.UserId AS NVARCHAR(36)) = @UserId AND i.Status = 'pending'
                ORDER BY i.CreatedAt DESC";

            var invitations = new List<object>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    invitations.Add(new
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        UserId = reader.GetString(2),
                        InvitedById = reader.GetString(3),
                        Role = reader.GetString(4),
                        Status = reader.GetString(5),
                        CreatedAt = reader.GetDateTime(6),
                        ProjectName = reader.GetString(7),
                        ProjectKey = reader.IsDBNull(8) ? null : reader.GetString(8)
                    });
                }
            }

            return Results.Ok(invitations);
        });

        // Listar invitaciones enviadas por el creador (para un proyecto)
        group.MapGet("/{id}/invitations", async (string id, string? userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verificar que el proyecto existe y obtener el creador
            string creatorId = "";
            using (var checkCmd = new SqlCommand("SELECT CAST(CreatorId AS NVARCHAR(36)) FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var result = await checkCmd.ExecuteScalarAsync();
                if (result == null)
                {
                    return Results.NotFound(new { message = "Proyecto no encontrado" });
                }
                creatorId = result.ToString()!;
            }

            // Solo el creador puede ver las invitaciones
            if (!string.IsNullOrWhiteSpace(userId) && creatorId != userId)
            {
                return Results.BadRequest(new { message = "Solo el creador puede ver las invitaciones" });
            }

            // Obtener invitaciones
            var sql = @"
                SELECT CAST(i.Id AS NVARCHAR(36)), CAST(i.ProjectId AS NVARCHAR(36)) as ProjectId,
                       CAST(i.UserId AS NVARCHAR(36)) as UserId, u.Name as UserName, u.Email as UserEmail,
                       CAST(i.InvitedById AS NVARCHAR(36)) as InvitedById, i.Role, i.Status, i.CreatedAt, i.RespondedAt
                FROM ProjectInvitations i
                INNER JOIN Users u ON i.UserId = u.Id
                WHERE CAST(i.ProjectId AS NVARCHAR(36)) = @ProjectId
                ORDER BY i.CreatedAt DESC";

            var invitations = new List<object>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@ProjectId", id);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    invitations.Add(new
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        UserId = reader.GetString(2),
                        UserName = reader.GetString(3),
                        UserEmail = reader.GetString(4),
                        InvitedById = reader.GetString(5),
                        Role = reader.GetString(6),
                        Status = reader.GetString(7),
                        CreatedAt = reader.GetDateTime(8),
                        RespondedAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9)
                    });
                }
            }

            return Results.Ok(invitations);
        });

        group.MapPost("/{id}/leave", async (string id, string userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verificar que existe el proyecto
            using (var checkCmd = new SqlCommand("SELECT CAST(CreatorId AS NVARCHAR(36)) FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var result = await checkCmd.ExecuteScalarAsync();
                if (result == null)
                {
                    return Results.NotFound(new { message = "Proyecto no encontrado" });
                }
                var creatorId = result.ToString()?.ToUpperInvariant();
                if (creatorId == userId.ToUpperInvariant())
                {
                    return Results.BadRequest(new { message = "El creador no puede salir del proyecto. Debe eliminarlo o transferir la propiedad." });
                }
            }

            // Eliminar miembro del proyecto
            using (var deleteCmd = new SqlCommand(@"
                DELETE FROM ProjectMembers 
                WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId AND CAST(UserId AS NVARCHAR(36)) = @UserId", connection))
            {
                deleteCmd.Parameters.AddWithValue("@ProjectId", id);
                deleteCmd.Parameters.AddWithValue("@UserId", userId);
                var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();
                if (rowsAffected == 0)
                {
                    return Results.NotFound(new { message = "No eres miembro de este proyecto" });
                }
            }

            return Results.Ok(new { message = "Has salido del proyecto" });
        });

        group.MapPut("/{id}", async (string id, UpdateProjectRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verificar que el proyecto existe y el usuario es el creador
            using (var checkCmd = new SqlCommand("SELECT CAST(CreatorId AS NVARCHAR(36)) FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var result = await checkCmd.ExecuteScalarAsync();
                if (result == null)
                {
                    return Results.NotFound(new { message = "Proyecto no encontrado" });
                }
                var creatorId = result.ToString();
                if (!string.IsNullOrWhiteSpace(request.UserId) && creatorId != request.UserId)
                {
                    return Results.BadRequest(new { message = "Solo el creador puede actualizar el proyecto" });
                }
            }

            // Actualizar proyecto
            using (var updateCmd = new SqlCommand(@"
                UPDATE Projects 
                SET Name = @Name, Description = @Description, [Key] = @Key, Color = @Color, Icon = @Icon
                WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@Name", request.Name.Trim());
                updateCmd.Parameters.AddWithValue("@Description", (object?)request.Description?.Trim() ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Key", string.IsNullOrWhiteSpace(request.Key) ? DBNull.Value : request.Key.Trim().ToUpperInvariant());
                updateCmd.Parameters.AddWithValue("@Color", (object?)request.Color ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Icon", (object?)request.Icon ?? DBNull.Value);
                await updateCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Proyecto actualizado" });
        });

        group.MapDelete("/{id}", async (string id, string? userId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Verificar que el proyecto existe y el usuario es el creador
                using (var checkCmd = new SqlCommand("SELECT CAST(CreatorId AS NVARCHAR(36)) FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@Id", id);
                    var result = await checkCmd.ExecuteScalarAsync();
                    if (result == null)
                    {
                        transaction.Rollback();
                        return Results.NotFound(new { message = "Proyecto no encontrado" });
                    }
                    var creatorId = result.ToString();
                    if (!string.IsNullOrWhiteSpace(userId) && creatorId != userId)
                    {
                        transaction.Rollback();
                        return Results.BadRequest(new { message = "Solo el creador puede eliminar el proyecto" });
                    }
                }

                // Eliminar datos relacionados (en orden por dependencias de FK)
                var deleteCommands = new[]
                {
                    "DELETE FROM Notifications WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId",
                    "DELETE FROM RetrospectiveActionItems WHERE CAST(RetrospectiveId AS NVARCHAR(36)) IN (SELECT CAST(r.Id AS NVARCHAR(36)) FROM SprintRetrospectives r INNER JOIN Sprints s ON r.SprintId = s.Id WHERE CAST(s.ProjectId AS NVARCHAR(36)) = @ProjectId)",
                    "DELETE FROM RetrospectiveItems WHERE CAST(RetrospectiveId AS NVARCHAR(36)) IN (SELECT CAST(r.Id AS NVARCHAR(36)) FROM SprintRetrospectives r INNER JOIN Sprints s ON r.SprintId = s.Id WHERE CAST(s.ProjectId AS NVARCHAR(36)) = @ProjectId)",
                    "DELETE FROM SprintRetrospectives WHERE CAST(SprintId AS NVARCHAR(36)) IN (SELECT CAST(Id AS NVARCHAR(36)) FROM Sprints WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId)",
                    "DELETE FROM BurndownData WHERE CAST(SprintId AS NVARCHAR(36)) IN (SELECT CAST(Id AS NVARCHAR(36)) FROM Sprints WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId)",
                    "DELETE FROM StandupNotes WHERE CAST(SprintId AS NVARCHAR(36)) IN (SELECT CAST(Id AS NVARCHAR(36)) FROM Sprints WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId)",
                    "DELETE FROM Tasks WHERE CAST(StoryId AS NVARCHAR(36)) IN (SELECT CAST(Id AS NVARCHAR(36)) FROM UserStories WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId)",
                    "DELETE FROM StoryComments WHERE CAST(StoryId AS NVARCHAR(36)) IN (SELECT CAST(Id AS NVARCHAR(36)) FROM UserStories WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId)",
                    "DELETE FROM StoryHistory WHERE CAST(StoryId AS NVARCHAR(36)) IN (SELECT CAST(Id AS NVARCHAR(36)) FROM UserStories WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId)",
                    "DELETE FROM UserStories WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId",
                    "DELETE FROM Sprints WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId",
                    "DELETE FROM ProjectInvitations WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId",
                    "DELETE FROM ProjectMembers WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId",
                    "DELETE FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @ProjectId"
                };

                foreach (var sql in deleteCommands)
                {
                    using var cmd = new SqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@ProjectId", id);
                    await cmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                return Results.Ok(new { message = "Proyecto eliminado" });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Results.Problem($"Error al eliminar proyecto: {ex.Message}");
            }
        });
    }

    public static ProjectDto ToProjectDto(Project project, AppDataStore store)
    {
        var creator = store.Data.Users.FirstOrDefault(user => user.Id == project.CreatorId);
        var members = store.Data.ProjectMembers
            .Where(member => member.ProjectId == project.Id)
            .Join(store.Data.Users, member => member.UserId, user => user.Id, (member, user) => new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                CreatedAt = user.CreatedAt
            })
            .OrderBy(user => user.Name)
            .ToList();

        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Key = project.Key,
            Color = project.Color,
            Icon = project.Icon,
            CreatorId = project.CreatorId,
            ProductOwnerId = project.CreatorId,
            CreatorName = creator?.Name,
            CreatedAt = project.CreatedAt,
            Members = members
        };
    }

    public static void CreateNotification(AppDataStore store, string userId, string title, string message, string type, string? projectId, string? creatorId, string status)
    {
        store.Data.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            ProjectId = projectId,
            CreatorId = creatorId,
            Status = status,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void AddMember(string projectId, string userId, string role, AppDataStore store)
    {
        if (store.Data.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == userId))
        {
            return;
        }

        store.Data.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTime.UtcNow
        });
    }

    private static string BuildProjectKey(string name)
    {
        var letters = new string(name
            .Where(char.IsLetterOrDigit)
            .Take(4)
            .ToArray())
            .ToUpperInvariant();

        return string.IsNullOrWhiteSpace(letters) ? "PROJ" : letters;
    }

    private static string NormalizeProjectRole(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            "product owner" or "productowner" => "Product Owner",
            "scrum master" or "scrummaster" => "Scrum Master",
            _ => "Developer"
        };
    }
}
