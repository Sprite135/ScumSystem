using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class SprintRoutes
{
    public static void MapSprintRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sprints");

        group.MapGet("/", async (DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(s.Id AS NVARCHAR(36)), CAST(s.ProjectId AS NVARCHAR(36)) as ProjectId, s.Name, s.Description,
                       s.StartDate, s.EndDate, s.Status, s.Goal, s.CreatedAt,
                       p.Name as ProjectName, p.[Key] as ProjectKey,
                       (SELECT COUNT(*) FROM UserStories WHERE CAST(SprintId AS NVARCHAR(36)) = CAST(s.Id AS NVARCHAR(36))) as StoryCount
                FROM Sprints s
                LEFT JOIN Projects p ON s.ProjectId = p.Id
                ORDER BY s.CreatedAt DESC";

            var sprints = new List<SprintDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    sprints.Add(new SprintDto
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        Name = reader.GetString(2),
                        Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                        StartDate = reader.GetDateTime(4),
                        EndDate = reader.GetDateTime(5),
                        Status = reader.GetString(6),
                        Goal = reader.IsDBNull(7) ? null : reader.GetString(7),
                        CreatedAt = reader.GetDateTime(8),
                        ProjectName = reader.IsDBNull(9) ? null : reader.GetString(9),
                        ProjectKey = reader.IsDBNull(10) ? null : reader.GetString(10),
                        StoryCount = reader.GetInt32(11)
                    });
                }
            }

            return Results.Ok(sprints);
        });

        group.MapGet("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sprintDto = await GetSprintByIdAsync(connection, id);
            return sprintDto is null ? Results.NotFound() : Results.Ok(sprintDto);
        });

        group.MapGet("/project/{projectId}", async (string projectId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(Id AS NVARCHAR(36)) as Id, CAST(ProjectId AS NVARCHAR(36)) as ProjectId, 
                       Name, Goal, StartDate, EndDate, DurationWeeks, Status, CreatedAt
                FROM Sprints
                WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId
                ORDER BY StartDate DESC";

            var sprints = new List<SprintDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@ProjectId", projectId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    sprints.Add(new SprintDto
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        Name = reader.GetString(2),
                        Goal = reader.IsDBNull(3) ? null : reader.GetString(3),
                        StartDate = reader.GetDateTime(4),
                        EndDate = reader.GetDateTime(5),
                        DurationWeeks = reader.GetInt32(6),
                        Status = reader.GetString(7),
                        CreatedAt = reader.GetDateTime(8)
                    });
                }
            }

            return Results.Ok(sprints);
        });

        group.MapGet("/{id}/burndown", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Get sprint info
            DateTime startDate, endDate;
            using (var sprintCmd = new SqlCommand("SELECT StartDate, EndDate FROM Sprints WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                sprintCmd.Parameters.AddWithValue("@Id", id);
                using var reader = await sprintCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return Results.NotFound();
                }
                startDate = reader.GetDateTime(0).Date;
                endDate = reader.GetDateTime(1).Date < startDate ? startDate : reader.GetDateTime(1).Date;
            }

            // Get stories for this sprint
            var totalPoints = 0;
            using (var storiesCmd = new SqlCommand("SELECT StoryPoints FROM UserStories WHERE CAST(SprintId AS NVARCHAR(36)) = @SprintId", connection))
            {
                storiesCmd.Parameters.AddWithValue("@SprintId", id);
                using var reader = await storiesCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        totalPoints += reader.GetInt32(0);
                    }
                }
            }

            var totalDays = Math.Max(1, (endDate - startDate).Days);
            var chart = new BurndownChartDto();
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                chart.Labels.Add(date.ToString("dd/MM"));
                var elapsedDays = (date - startDate).Days;
                var idealRemaining = totalPoints - ((decimal)totalPoints * elapsedDays / totalDays);
                chart.Ideal.Add(Math.Max(0, Math.Round(idealRemaining, 2)));

                // Calculate remaining points for this date
                var remaining = 0;
                using (var remainingCmd = new SqlCommand(@"
                    SELECT SUM(StoryPoints) FROM UserStories 
                    WHERE CAST(SprintId AS NVARCHAR(36)) = @SprintId 
                    AND (Status != 'Done' OR UpdatedAt > @Date)", connection))
                {
                    remainingCmd.Parameters.AddWithValue("@SprintId", id);
                    remainingCmd.Parameters.AddWithValue("@Date", date.AddDays(1)); // Next day at midnight
                    var result = await remainingCmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        remaining = Convert.ToInt32(result);
                    }
                }

                chart.Actual.Add(remaining);
            }

            return Results.Ok(chart);
        });

        group.MapPost("/", async (CreateSprintRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verify project exists in SQL
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @ProjectId", connection))
            {
                checkCmd.Parameters.AddWithValue("@ProjectId", request.ProjectId);
                var count = (int)await checkCmd.ExecuteScalarAsync();
                if (count == 0)
                {
                    return Results.BadRequest("El proyecto no existe");
                }
            }

            var sprintId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;
            var durationWeeks = Math.Max(1, (int)Math.Ceiling((request.EndDate.Date - request.StartDate.Date).TotalDays / 7d));
            var status = string.IsNullOrWhiteSpace(request.Status) ? "Planning" : request.Status;

            // Insert sprint
            var insertSql = @"
                INSERT INTO Sprints (Id, ProjectId, Name, Goal, StartDate, EndDate, DurationWeeks, Status, CreatedAt) 
                VALUES (@Id, @ProjectId, @Name, @Goal, @StartDate, @EndDate, @DurationWeeks, @Status, @CreatedAt)";

            using (var insertCmd = new SqlCommand(insertSql, connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", sprintId);
                insertCmd.Parameters.AddWithValue("@ProjectId", Guid.Parse(request.ProjectId));
                insertCmd.Parameters.AddWithValue("@Name", request.Name.Trim());
                insertCmd.Parameters.AddWithValue("@Goal", (object?)request.Goal?.Trim() ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@StartDate", request.StartDate);
                insertCmd.Parameters.AddWithValue("@EndDate", request.EndDate);
                insertCmd.Parameters.AddWithValue("@DurationWeeks", durationWeeks);
                insertCmd.Parameters.AddWithValue("@Status", status);
                insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync();
            }

            var sprintDto = await GetSprintByIdAsync(connection, sprintId.ToString());
            return Results.Created($"/api/sprints/{sprintId}", sprintDto);
        });

        group.MapPut("/{id}", async (string id, UpdateStatusRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if sprint exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Sprints WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var count = (int)await checkCmd.ExecuteScalarAsync();
                if (count == 0)
                {
                    return Results.NotFound();
                }
            }

            var sql = "UPDATE Sprints SET Status = @Status WHERE CAST(Id AS NVARCHAR(36)) = @Id";
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Status", request.Status);
                await cmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Sprint actualizado" });
        });

        group.MapDelete("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if sprint exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Sprints WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var count = (int)await checkCmd.ExecuteScalarAsync();
                if (count == 0)
                {
                    return Results.NotFound();
                }
            }

            // Move stories back to backlog
            using (var updateCmd = new SqlCommand(@"UPDATE UserStories 
                SET SprintId = NULL, Status = 'Backlog', UpdatedAt = @UpdatedAt 
                WHERE CAST(SprintId AS NVARCHAR(36)) = @Id", connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);
                await updateCmd.ExecuteNonQueryAsync();
            }

            // Delete sprint
            using (var deleteCmd = new SqlCommand("DELETE FROM Sprints WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                deleteCmd.Parameters.AddWithValue("@Id", id);
                await deleteCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Sprint eliminado" });
        });

        // Endpoint para completar sprint (cambiar estado a 'Completed' y mover historias incompletas al backlog)
        group.MapPost("/{id}/complete", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Verificar que el sprint existe y está activo
                using (var checkCmd = new SqlCommand("SELECT Status FROM Sprints WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@Id", id);
                    var result = await checkCmd.ExecuteScalarAsync();
                    if (result == null)
                    {
                        transaction.Rollback();
                        return Results.NotFound(new { message = "Sprint no encontrado" });
                    }
                    if (result.ToString() != "Active")
                    {
                        transaction.Rollback();
                        return Results.BadRequest(new { message = "El sprint no está activo" });
                    }
                }

                // Mover historias incompletas al backlog
                using (var updateStoriesCmd = new SqlCommand(@"
                    UPDATE UserStories 
                    SET SprintId = NULL, Status = 'Backlog', UpdatedAt = @UpdatedAt
                    WHERE CAST(SprintId AS NVARCHAR(36)) = @SprintId AND Status != 'Done'", connection, transaction))
                {
                    updateStoriesCmd.Parameters.AddWithValue("@SprintId", id);
                    updateStoriesCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);
                    await updateStoriesCmd.ExecuteNonQueryAsync();
                }

                // Actualizar estado del sprint a 'Completed'
                using (var updateSprintCmd = new SqlCommand(@"
                    UPDATE Sprints 
                    SET Status = 'Completed', UpdatedAt = @UpdatedAt, EndDate = @EndDate
                    WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection, transaction))
                {
                    updateSprintCmd.Parameters.AddWithValue("@Id", id);
                    updateSprintCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);
                    updateSprintCmd.Parameters.AddWithValue("@EndDate", DateTime.UtcNow);
                    await updateSprintCmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                return Results.Ok(new { message = "Sprint completado exitosamente. Las historias incompletas se han movido al backlog." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Results.Problem($"Error al completar sprint: {ex.Message}");
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

    private static async Task<SprintDto?> GetSprintByIdAsync(SqlConnection connection, string sprintId)
    {
        var sql = @"
            SELECT CAST(s.Id AS NVARCHAR(36)) as Id, CAST(s.ProjectId AS NVARCHAR(36)) as ProjectId, 
                   s.Name, s.Goal, s.StartDate, s.EndDate, s.DurationWeeks, s.Status, s.CreatedAt
            FROM Sprints s
            WHERE CAST(s.Id AS NVARCHAR(36)) = @SprintId";

        using (var cmd = new SqlCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@SprintId", sprintId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new SprintDto
                {
                    Id = reader.GetString(0),
                    ProjectId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Goal = reader.IsDBNull(3) ? null : reader.GetString(3),
                    StartDate = reader.GetDateTime(4),
                    EndDate = reader.GetDateTime(5),
                    DurationWeeks = reader.GetInt32(6),
                    Status = reader.GetString(7),
                    CreatedAt = reader.GetDateTime(8)
                };
            }
        }
        return null;
    }
}
