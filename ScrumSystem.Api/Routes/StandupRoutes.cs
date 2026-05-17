using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class StandupRoutes
{
    public static void MapStandupRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/standup");

        group.MapPost("/", async (CreateStandupRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var noteId = Guid.NewGuid();
            var noteDate = request.Date == default ? DateTime.UtcNow.Date : request.Date.Date;

            using (var insertCmd = new SqlCommand(@"
                INSERT INTO StandupNotes (Id, SprintId, UserId, Date, Yesterday, Today, Blockers, CreatedAt) 
                VALUES (@Id, @SprintId, @UserId, @Date, @Yesterday, @Today, @Blockers, @CreatedAt)", connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", noteId);
                insertCmd.Parameters.AddWithValue("@SprintId", Guid.Parse(request.SprintId));
                insertCmd.Parameters.AddWithValue("@UserId", Guid.Parse(request.UserId));
                insertCmd.Parameters.AddWithValue("@Date", noteDate);
                insertCmd.Parameters.AddWithValue("@Yesterday", (object?)request.Yesterday?.Trim() ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@Today", (object?)request.Today?.Trim() ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@Blockers", (object?)request.Blockers?.Trim() ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
                await insertCmd.ExecuteNonQueryAsync();
            }

            return Results.Created($"/api/standup/{noteId}", new { id = noteId.ToString(), message = "Nota de standup creada exitosamente" });
        });

        group.MapGet("/sprint/{sprintId}", async (string sprintId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(n.Id AS NVARCHAR(36)) as Id, CAST(n.SprintId AS NVARCHAR(36)) as SprintId, CAST(n.UserId AS NVARCHAR(36)) as UserId,
                       n.Date, n.Yesterday, n.Today, n.Blockers, n.CreatedAt, u.Name as UserName
                FROM StandupNotes n
                LEFT JOIN Users u ON n.UserId = u.Id
                WHERE CAST(n.SprintId AS NVARCHAR(36)) = @SprintId
                ORDER BY n.CreatedAt DESC";

            var notes = new List<StandupNoteDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@SprintId", sprintId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    notes.Add(new StandupNoteDto
                    {
                        Id = reader.GetString(0),
                        SprintId = reader.GetString(1),
                        UserId = reader.GetString(2),
                        Date = reader.GetDateTime(3),
                        Yesterday = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Today = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Blockers = reader.IsDBNull(6) ? null : reader.GetString(6),
                        CreatedAt = reader.GetDateTime(7),
                        UserName = reader.IsDBNull(8) ? null : reader.GetString(8)
                    });
                }
            }

            return Results.Ok(notes);
        });

        group.MapGet("/sprint/{sprintId}/today", async (string sprintId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var today = DateTime.UtcNow.Date;
            var sql = @"
                SELECT CAST(n.Id AS NVARCHAR(36)) as Id, CAST(n.SprintId AS NVARCHAR(36)) as SprintId, CAST(n.UserId AS NVARCHAR(36)) as UserId,
                       n.Date, n.Yesterday, n.Today, n.Blockers, n.CreatedAt, u.Name as UserName
                FROM StandupNotes n
                LEFT JOIN Users u ON n.UserId = u.Id
                WHERE CAST(n.SprintId AS NVARCHAR(36)) = @SprintId AND CAST(n.CreatedAt AS DATE) = @Today
                ORDER BY n.CreatedAt DESC";

            var notes = new List<StandupNoteDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@SprintId", sprintId);
                cmd.Parameters.AddWithValue("@Today", today);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    notes.Add(new StandupNoteDto
                    {
                        Id = reader.GetString(0),
                        SprintId = reader.GetString(1),
                        UserId = reader.GetString(2),
                        Date = reader.GetDateTime(3),
                        Yesterday = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Today = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Blockers = reader.IsDBNull(6) ? null : reader.GetString(6),
                        CreatedAt = reader.GetDateTime(7),
                        UserName = reader.IsDBNull(8) ? null : reader.GetString(8)
                    });
                }
            }

            return Results.Ok(notes);
        });

        group.MapPatch("/{id}", async (string id, CreateStandupRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using (var updateCmd = new SqlCommand(@"
                UPDATE StandupNotes 
                SET Yesterday = @Yesterday, Today = @Today, Blockers = @Blockers
                WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", Guid.Parse(id));
                updateCmd.Parameters.AddWithValue("@Yesterday", (object?)request.Yesterday?.Trim() ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Today", (object?)request.Today?.Trim() ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Blockers", (object?)request.Blockers?.Trim() ?? DBNull.Value);
                var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                
                if (rowsAffected == 0)
                {
                    return Results.NotFound();
                }
            }

            return Results.Ok(new { message = "Nota de standup actualizada exitosamente" });
        });

        group.MapGet("/sprint/{sprintId}/missing", async (string sprintId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Obtener el ProjectId del sprint
            string projectId = "";
            using (var sprintCmd = new SqlCommand("SELECT CAST(ProjectId AS NVARCHAR(36)) FROM Sprints WHERE CAST(Id AS NVARCHAR(36)) = @SprintId", connection))
            {
                sprintCmd.Parameters.AddWithValue("@SprintId", sprintId);
                var result = await sprintCmd.ExecuteScalarAsync();
                if (result == null)
                {
                    return Results.NotFound(new { message = "Sprint no encontrado" });
                }
                projectId = result.ToString()!;
            }

            // Obtener usuarios que NO han completado standup hoy
            var today = DateTime.Today;
            var sql = @"
                SELECT CAST(u.Id AS NVARCHAR(36)), u.Name, u.Email, u.Role, u.CreatedAt
                FROM ProjectMembers pm
                INNER JOIN Users u ON pm.UserId = u.Id
                WHERE CAST(pm.ProjectId AS NVARCHAR(36)) = @ProjectId
                  AND CAST(pm.UserId AS NVARCHAR(36)) NOT IN (
                      SELECT CAST(UserId AS NVARCHAR(36)) 
                      FROM StandupNotes 
                      WHERE CAST(SprintId AS NVARCHAR(36)) = @SprintId AND CAST(Date AS DATE) = @Today
                  )
                ORDER BY u.Name";

            var users = new List<UserDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@ProjectId", projectId);
                cmd.Parameters.AddWithValue("@SprintId", sprintId);
                cmd.Parameters.AddWithValue("@Today", today);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    users.Add(new UserDto
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Email = reader.GetString(2),
                        Role = Enum.Parse<UserRole>(reader.GetString(3)),
                        CreatedAt = reader.GetDateTime(4)
                    });
                }
            }

            return Results.Ok(users);
        });
    }

    private static StandupNoteDto ToStandupDto(StandupNote note, AppDataStore store)
    {
        var user = store.Data.Users.FirstOrDefault(item => item.Id == note.UserId);
        return new StandupNoteDto
        {
            Id = note.Id,
            SprintId = note.SprintId,
            UserId = note.UserId,
            Date = note.Date,
            Yesterday = note.Yesterday,
            Today = note.Today,
            Blockers = note.Blockers,
            CreatedAt = note.CreatedAt,
            UserName = user?.Name
        };
    }
}
