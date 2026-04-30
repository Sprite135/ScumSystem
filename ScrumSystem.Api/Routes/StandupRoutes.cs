using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class StandupRoutes
{
    public static void MapStandupRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/standup");

        // Create standup note
        group.MapPost("/", async (CreateStandupRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var id = Guid.NewGuid();

                var sql = @"
                    INSERT INTO StandupNotes (Id, SprintId, UserId, Date, Yesterday, Today, Blockers)
                    VALUES (@Id, @SprintId, @UserId, @Date, @Yesterday, @Today, @Blockers)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@SprintId", request.SprintId);
                cmd.Parameters.AddWithValue("@UserId", request.UserId);
                cmd.Parameters.AddWithValue("@Date", request.Date.Date);
                cmd.Parameters.AddWithValue("@Yesterday", (object?)request.Yesterday ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Today", (object?)request.Today ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Blockers", (object?)request.Blockers ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();

                return Results.Created($"/api/standup/{id}", new StandupNote
                {
                    Id = id,
                    SprintId = request.SprintId,
                    UserId = request.UserId,
                    Date = request.Date,
                    Yesterday = request.Yesterday,
                    Today = request.Today,
                    Blockers = request.Blockers,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating note: {ex.Message}");
            }
        });

        // Get standup notes for sprint
        group.MapGet("/sprint/{sprintId:guid}", async (Guid sprintId, DatabaseContext db) =>
        {
            var notes = new List<StandupNoteDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT sn.*, u.Name as UserName
                FROM StandupNotes sn
                JOIN Users u ON sn.UserId = u.Id
                WHERE sn.SprintId = @SprintId
                ORDER BY sn.Date DESC, u.Name";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SprintId", sprintId);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                notes.Add(new StandupNoteDto
                {
                    Id = (Guid)reader["Id"],
                    SprintId = (Guid)reader["SprintId"],
                    UserId = (Guid)reader["UserId"],
                    Date = (DateTime)reader["Date"],
                    Yesterday = reader["Yesterday"]?.ToString(),
                    Today = reader["Today"]?.ToString(),
                    Blockers = reader["Blockers"]?.ToString(),
                    CreatedAt = (DateTime)reader["CreatedAt"],
                    UserName = reader["UserName"]?.ToString()
                });
            }

            return Results.Ok(notes);
        });

        // Get today's standup notes
        group.MapGet("/sprint/{sprintId:guid}/today", async (Guid sprintId, DatabaseContext db) =>
        {
            var notes = new List<StandupNoteDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var today = DateTime.Today;

            var sql = @"
                SELECT sn.*, u.Name as UserName
                FROM StandupNotes sn
                JOIN Users u ON sn.UserId = u.Id
                WHERE sn.SprintId = @SprintId AND CAST(sn.Date as DATE) = @Today
                ORDER BY u.Name";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SprintId", sprintId);
            cmd.Parameters.AddWithValue("@Today", today);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                notes.Add(new StandupNoteDto
                {
                    Id = (Guid)reader["Id"],
                    SprintId = (Guid)reader["SprintId"],
                    UserId = (Guid)reader["UserId"],
                    Date = (DateTime)reader["Date"],
                    Yesterday = reader["Yesterday"]?.ToString(),
                    Today = reader["Today"]?.ToString(),
                    Blockers = reader["Blockers"]?.ToString(),
                    CreatedAt = (DateTime)reader["CreatedAt"],
                    UserName = reader["UserName"]?.ToString()
                });
            }

            return Results.Ok(notes);
        });

        // Update standup note
        group.MapPatch("/{id:guid}", async (Guid id, CreateStandupRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = @"
                    UPDATE StandupNotes 
                    SET Yesterday = @Yesterday, Today = @Today, Blockers = @Blockers
                    WHERE Id = @Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Yesterday", (object?)request.Yesterday ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Today", (object?)request.Today ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Blockers", (object?)request.Blockers ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", id);

                await cmd.ExecuteNonQueryAsync();
                return Results.Ok(new { message = "Note updated" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error updating note: {ex.Message}");
            }
        });

        // Get sprint team members without standup today
        group.MapGet("/sprint/{sprintId:guid}/missing", async (Guid sprintId, DatabaseContext db) =>
        {
            var users = new List<UserDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var today = DateTime.Today;

            var sql = @"
                SELECT u.Id, u.Name, u.Email, u.Role, u.CreatedAt
                FROM Users u
                JOIN ProjectMembers pm ON u.Id = pm.UserId
                JOIN Sprints s ON pm.ProjectId = s.ProjectId
                WHERE s.Id = @SprintId
                AND u.Id NOT IN (
                    SELECT UserId FROM StandupNotes 
                    WHERE SprintId = @SprintId AND CAST(Date as DATE) = @Today
                )
                ORDER BY u.Name";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SprintId", sprintId);
            cmd.Parameters.AddWithValue("@Today", today);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                users.Add(new UserDto
                {
                    Id = (Guid)reader["Id"],
                    Name = reader["Name"].ToString()!,
                    Email = reader["Email"].ToString()!,
                    Role = Enum.Parse<UserRole>(reader["Role"].ToString()!),
                    CreatedAt = (DateTime)reader["CreatedAt"]
                });
            }

            return Results.Ok(users);
        });
    }
}
