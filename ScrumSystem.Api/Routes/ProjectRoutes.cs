using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class ProjectRoutes
{
    public static void MapProjectRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects");

        // Create project
        group.MapPost("/", async (CreateProjectRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var id = Guid.NewGuid();

                var sql = @"
                    INSERT INTO Projects (Id, Name, Description, [Key], Color, Icon, ProductOwnerId)
                    VALUES (@Id, @Name, @Description, @Key, @Color, @Icon, @ProductOwnerId)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Name", request.Name);
                cmd.Parameters.AddWithValue("@Description", (object?)request.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Key", (object?)request.Key ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Color", (object?)request.Color ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Icon", (object?)request.Icon ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ProductOwnerId", request.CreatedById.HasValue ? (object)request.CreatedById.Value : DBNull.Value);

                await cmd.ExecuteNonQueryAsync();

                // Add creator as member (always)
                if (request.CreatedById.HasValue)
                {
                    var memberSql = @"
                        INSERT INTO ProjectMembers (ProjectId, UserId) 
                        VALUES (@ProjectId, @UserId)";
                    using var memberCmd = new SqlCommand(memberSql, conn);
                    memberCmd.Parameters.AddWithValue("@ProjectId", id);
                    memberCmd.Parameters.AddWithValue("@UserId", request.CreatedById.Value);
                    await memberCmd.ExecuteNonQueryAsync();
                }

                // Add additional members from request
                if (request.MemberIds != null)
                {
                    foreach (var memberId in request.MemberIds)
                    {
                        if (memberId == request.CreatedById) continue; // Skip if already added
                        var memberSql = @"
                            INSERT INTO ProjectMembers (ProjectId, UserId) 
                            VALUES (@ProjectId, @UserId)";
                        using var memberCmd = new SqlCommand(memberSql, conn);
                        memberCmd.Parameters.AddWithValue("@ProjectId", id);
                        memberCmd.Parameters.AddWithValue("@UserId", memberId);
                        await memberCmd.ExecuteNonQueryAsync();
                    }
                }

                return Results.Created($"/api/projects/{id}", new Project
                {
                    Id = id,
                    Name = request.Name,
                    Description = request.Description,
                    Key = request.Key,
                    Color = request.Color,
                    Icon = request.Icon,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating project: {ex.Message}");
            }
        });

        // Update project
        group.MapPut("/{id:guid}", async (Guid id, UpdateProjectRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Check if user is the creator
                var checkSql = "SELECT ProductOwnerId FROM Projects WHERE Id = @Id";
                using var checkCmd = new SqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@Id", id);
                var productOwnerId = await checkCmd.ExecuteScalarAsync() as Guid?;

                if (productOwnerId == null)
                {
                    return Results.NotFound();
                }

                if (productOwnerId != request.UserId)
                {
                    return Results.Problem("Solo el creador del proyecto puede modificarlo", statusCode: 403);
                }

                var sql = @"
                    UPDATE Projects 
                    SET Name = @Name, [Key] = @Key, Color = @Color, Icon = @Icon
                    WHERE Id = @Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Name", request.Name);
                cmd.Parameters.AddWithValue("@Key", (object?)request.Key ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Color", (object?)request.Color ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Icon", (object?)request.Icon ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();

                return Results.Ok(new { message = "Proyecto actualizado" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error updating project: {ex.Message}");
            }
        });

        // Get all projects with members (filtered by user membership)
        group.MapGet("/", async (Guid userId, DatabaseContext db) =>
        {
            var projectsWithMembers = new List<ProjectDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            // Get projects where user is a member (creator or added via ProjectMembers)
            var sql = @"
                SELECT DISTINCT 
                    p.Id, p.Name, p.Description, p.[Key], p.Color, p.Icon, 
                    p.ProductOwnerId, p.ScrumMasterId, p.CreatedAt,
                    u.Name as CreatorName
                FROM Projects p
                LEFT JOIN ProjectMembers pm ON p.Id = pm.ProjectId
                LEFT JOIN Users u ON p.ProductOwnerId = u.Id
                WHERE p.ProductOwnerId = @UserId OR pm.UserId = @UserId
                ORDER BY p.CreatedAt DESC";
            
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            using var reader = await cmd.ExecuteReaderAsync();

            var projects = new List<ProjectDto>();
            while (await reader.ReadAsync())
            {
                var ownerIdValue = reader["ProductOwnerId"];
                var scrumMasterIdValue = reader["ScrumMasterId"];
                
                var project = new ProjectDto
                {
                    Id = (Guid)reader["Id"],
                    Name = reader["Name"].ToString()!,
                    Description = reader["Description"]?.ToString(),
                    Key = reader["Key"]?.ToString(),
                    Color = reader["Color"]?.ToString(),
                    Icon = reader["Icon"]?.ToString(),
                    ProductOwnerId = ownerIdValue == null || ownerIdValue == DBNull.Value ? null : (Guid?)ownerIdValue,
                    ScrumMasterId = scrumMasterIdValue == null || scrumMasterIdValue == DBNull.Value ? null : (Guid?)scrumMasterIdValue,
                    CreatedAt = (DateTime)reader["CreatedAt"],
                    CreatorName = reader["CreatorName"]?.ToString()
                };
                projects.Add(project);
            }
            reader.Close();

            // Get members for each project
            foreach (var project in projects)
            {
                var members = new List<UserDto>();
                var membersSql = @"
                    SELECT u.Id, u.Name, u.Email, u.Role, u.CreatedAt
                    FROM Users u
                    JOIN ProjectMembers pm ON u.Id = pm.UserId
                    WHERE pm.ProjectId = @ProjectId";
                
                using var membersCmd = new SqlCommand(membersSql, conn);
                membersCmd.Parameters.AddWithValue("@ProjectId", project.Id);
                using var membersReader = await membersCmd.ExecuteReaderAsync();
                
                while (await membersReader.ReadAsync())
                {
                    members.Add(new UserDto
                    {
                        Id = (Guid)membersReader["Id"],
                        Name = membersReader["Name"].ToString()!,
                        Email = membersReader["Email"].ToString()!,
                        Role = Enum.Parse<UserRole>(membersReader["Role"].ToString()!),
                        CreatedAt = (DateTime)membersReader["CreatedAt"]
                    });
                }
                membersReader.Close();

                projectsWithMembers.Add(new ProjectDto
                {
                    Id = project.Id,
                    Name = project.Name,
                    Description = project.Description,
                    Key = project.Key,
                    Color = project.Color,
                    Icon = project.Icon,
                    ProductOwnerId = project.ProductOwnerId,
                    ScrumMasterId = project.ScrumMasterId,
                    CreatedAt = project.CreatedAt,
                    CreatorName = project.CreatorName,
                    Members = members
                });
            }

            return Results.Ok(projectsWithMembers);
        });

        // Get project by ID with members (verify user has access)
        group.MapGet("/{id:guid}", async (Guid id, Guid userId, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            // Get project with creator name
            var sql = @"
                SELECT p.*, u.Name as CreatorName
                FROM Projects p
                LEFT JOIN Users u ON p.ProductOwnerId = u.Id
                WHERE p.Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.NotFound();
            }

            var project = new ProjectDto
            {
                Id = (Guid)reader["Id"],
                Name = reader["Name"].ToString()!,
                Description = reader["Description"]?.ToString(),
                Key = reader["Key"]?.ToString(),
                Color = reader["Color"]?.ToString(),
                Icon = reader["Icon"]?.ToString(),
                ProductOwnerId = reader["ProductOwnerId"] as Guid?,
                ScrumMasterId = reader["ScrumMasterId"] as Guid?,
                CreatedAt = (DateTime)reader["CreatedAt"],
                CreatorName = reader["CreatorName"]?.ToString()
            };
            reader.Close();

            // Verify user has access (is creator or member)
            var accessSql = @"
                SELECT COUNT(*) 
                FROM Projects p
                LEFT JOIN ProjectMembers pm ON p.Id = pm.ProjectId
                WHERE p.Id = @ProjectId AND (p.ProductOwnerId = @UserId OR pm.UserId = @UserId)";
            using var accessCmd = new SqlCommand(accessSql, conn);
            accessCmd.Parameters.AddWithValue("@ProjectId", id);
            accessCmd.Parameters.AddWithValue("@UserId", userId);
            var hasAccess = (int)await accessCmd.ExecuteScalarAsync() > 0;

            if (!hasAccess)
            {
                return Results.NotFound(); // Return 404 to avoid leaking project existence
            }

            // Get members
            var membersSql = @"
                SELECT u.Id, u.Name, u.Email, u.Role, u.CreatedAt
                FROM Users u
                JOIN ProjectMembers pm ON u.Id = pm.UserId
                WHERE pm.ProjectId = @ProjectId";

            using var membersCmd = new SqlCommand(membersSql, conn);
            membersCmd.Parameters.AddWithValue("@ProjectId", id);
            using var membersReader = await membersCmd.ExecuteReaderAsync();

            var members = new List<UserDto>();
            while (await membersReader.ReadAsync())
            {
                members.Add(new UserDto
                {
                    Id = (Guid)membersReader["Id"],
                    Name = membersReader["Name"].ToString()!,
                    Email = membersReader["Email"].ToString()!,
                    Role = Enum.Parse<UserRole>(membersReader["Role"].ToString()!),
                    CreatedAt = (DateTime)membersReader["CreatedAt"]
                });
            }

            return Results.Ok(new ProjectDto
            {
                Id = project.Id,
                Name = project.Name,
                Description = project.Description,
                Key = project.Key,
                Color = project.Color,
                Icon = project.Icon,
                ProductOwnerId = project.ProductOwnerId,
                ScrumMasterId = project.ScrumMasterId,
                CreatedAt = project.CreatedAt,
                Members = members
            });
        });

        // Add member to project (direct add, creates notification)
        group.MapPost("/{id:guid}/members", async (Guid id, AddMemberRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Check if user is already a member
                var checkSql = "SELECT COUNT(*) FROM ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @UserId";
                using var checkCmd = new SqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@ProjectId", id);
                checkCmd.Parameters.AddWithValue("@UserId", request.UserId);
                var count = (int)await checkCmd.ExecuteScalarAsync();

                if (count > 0)
                {
                    return Results.Problem("El usuario ya es miembro del proyecto", statusCode: 400);
                }

                // Get project name for notification
                var projectSql = "SELECT Name FROM Projects WHERE Id = @Id";
                using var projectCmd = new SqlCommand(projectSql, conn);
                projectCmd.Parameters.AddWithValue("@Id", id);
                var projectName = await projectCmd.ExecuteScalarAsync() as string ?? "Proyecto";

                // Create notification for the added user (pending invitation)
                var notificationId = Guid.NewGuid();
                var notificationSql = @"
                    INSERT INTO Notifications (Id, UserId, Type, Title, Message, ProjectId, Status, IsRead, CreatedAt)
                    VALUES (@Id, @UserId, @Type, @Title, @Message, @ProjectId, 'pending', 0, GETUTCDATE())";
                
                using var notifCmd = new SqlCommand(notificationSql, conn);
                notifCmd.Parameters.AddWithValue("@Id", notificationId);
                notifCmd.Parameters.AddWithValue("@UserId", request.UserId);
                notifCmd.Parameters.AddWithValue("@Type", "project_invitation");
                notifCmd.Parameters.AddWithValue("@Title", "¡Invitación a proyecto!");
                notifCmd.Parameters.AddWithValue("@Message", "Has sido invitado a unirte al proyecto \"" + projectName + "\". Acepta o rechaza la invitación.");
                notifCmd.Parameters.AddWithValue("@ProjectId", id);
                
                await notifCmd.ExecuteNonQueryAsync();

                return Results.Ok(new { message = "Invitación enviada exitosamente" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error al enviar invitación: {ex.Message}");
            }
        });

        // Delete project (only creator can delete)
        group.MapDelete("/{id:guid}", async (Guid id, Guid userId, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Check if user is the creator
                var checkSql = "SELECT ProductOwnerId FROM Projects WHERE Id = @Id";
                using var checkCmd = new SqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@Id", id);
                var productOwnerId = await checkCmd.ExecuteScalarAsync() as Guid?;

                // If project has no owner (legacy), allow any member to delete
                // If project has owner, only owner can delete
                if (productOwnerId != null && productOwnerId != userId)
                {
                    return Results.Problem("Solo el creador del proyecto puede eliminarlo", statusCode: 403);
                }

                Console.WriteLine($"[DELETE PROJECT] Attempting to delete project {id}");

                // Get project name and members for notification
                var projectInfoSql = "SELECT Name FROM Projects WHERE Id = @Id";
                using var projectInfoCmd = new SqlCommand(projectInfoSql, conn);
                projectInfoCmd.Parameters.AddWithValue("@Id", id);
                var projectName = await projectInfoCmd.ExecuteScalarAsync() as string ?? "Proyecto";

                var membersSql = "SELECT UserId FROM ProjectMembers WHERE ProjectId = @Id";
                using var membersCmd = new SqlCommand(membersSql, conn);
                membersCmd.Parameters.AddWithValue("@Id", id);
                using var membersReader = await membersCmd.ExecuteReaderAsync();
                var memberIds = new List<Guid>();
                while (await membersReader.ReadAsync())
                {
                    memberIds.Add((Guid)membersReader["UserId"]);
                }
                membersReader.Close();

                // Send notification to all members (except the deleter)
                foreach (var memberId in memberIds)
                {
                    if (memberId == userId) continue; // Don't notify the deleter

                    var notificationId = Guid.NewGuid();
                    var notificationSql = @"
                        INSERT INTO Notifications (Id, UserId, Type, Title, Message, Status, IsRead, CreatedAt)
                        VALUES (@Id, @UserId, @Type, @Title, @Message, 'completed', 0, GETUTCDATE())";

                    using var notifCmd = new SqlCommand(notificationSql, conn);
                    notifCmd.Parameters.AddWithValue("@Id", notificationId);
                    notifCmd.Parameters.AddWithValue("@UserId", memberId);
                    notifCmd.Parameters.AddWithValue("@Type", "project_deleted");
                    notifCmd.Parameters.AddWithValue("@Title", "Proyecto eliminado");
                    notifCmd.Parameters.AddWithValue("@Message", $"El proyecto \"{projectName}\" ha sido eliminado por el creador.");
                    await notifCmd.ExecuteNonQueryAsync();
                }

                // Delete in correct order to handle foreign key constraints
                var sqls = new[]
                {
                    "DELETE FROM StandupNotes WHERE SprintId IN (SELECT Id FROM Sprints WHERE ProjectId = @Id)",
                    "DELETE FROM Tasks WHERE StoryId IN (SELECT Id FROM UserStories WHERE ProjectId = @Id)",
                    "DELETE FROM UserStories WHERE ProjectId = @Id",
                    "DELETE FROM Sprints WHERE ProjectId = @Id",
                    "DELETE FROM ProjectMembers WHERE ProjectId = @Id",
                    "DELETE FROM Projects WHERE Id = @Id"
                };

                foreach (var sql in sqls)
                {
                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Id", id);
                    var rows = await cmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"[DELETE PROJECT] SQL: {sql.Substring(0, Math.Min(50, sql.Length))}... Rows affected: {rows}");
                }

                Console.WriteLine($"[DELETE PROJECT] Successfully deleted project {id}");
                return Results.Ok(new { message = "Proyecto eliminado exitosamente" });
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"[DELETE PROJECT] SQL Error: {ex.Message} - Number: {ex.Number}");
                return Results.Problem($"Error de base de datos al eliminar proyecto: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DELETE PROJECT] General Error: {ex.Message} - Stack: {ex.StackTrace}");
                return Results.Problem($"Error al eliminar proyecto: {ex.Message}");
            }
        });

        // Leave project (remove self from members)
        group.MapPost("/{id:guid}/leave", async (Guid id, Guid userId, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Check if user is the creator (creator cannot leave, must delete)
                var checkSql = "SELECT ProductOwnerId FROM Projects WHERE Id = @Id";
                using var checkCmd = new SqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@Id", id);
                var productOwnerId = await checkCmd.ExecuteScalarAsync() as Guid?;

                if (productOwnerId == userId)
                {
                    return Results.Problem("El creador no puede abandonar el proyecto, solo eliminarlo", statusCode: 400);
                }

                // Remove user from project members
                var deleteSql = "DELETE FROM ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @UserId";
                using var deleteCmd = new SqlCommand(deleteSql, conn);
                deleteCmd.Parameters.AddWithValue("@ProjectId", id);
                deleteCmd.Parameters.AddWithValue("@UserId", userId);
                
                var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();
                
                if (rowsAffected == 0)
                {
                    return Results.NotFound("No eres miembro de este proyecto");
                }

                return Results.Ok(new { message = "Has abandonado el proyecto exitosamente" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error al abandonar proyecto: {ex.Message}");
            }
        });
    }

    private static Project MapProject(SqlDataReader reader)
    {
        return new Project
        {
            Id = (Guid)reader["Id"],
            Name = reader["Name"].ToString()!,
            Description = reader["Description"]?.ToString(),
            Key = reader["Key"]?.ToString(),
            Color = reader["Color"]?.ToString(),
            Icon = reader["Icon"]?.ToString(),
            ProductOwnerId = reader["ProductOwnerId"] as Guid?,
            ScrumMasterId = reader["ScrumMasterId"] as Guid?,
            CreatedAt = (DateTime)reader["CreatedAt"]
        };
    }
}

public class AddMemberRequest
{
    public Guid UserId { get; set; }
}
