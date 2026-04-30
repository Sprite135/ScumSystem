using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class SprintRoutes
{
    public static void MapSprintRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sprints");

        // Get all sprints
        group.MapGet("/", async (DatabaseContext db) =>
        {
            var sprints = new List<SprintDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT s.*,
                    ISNULL((SELECT SUM(StoryPoints) FROM UserStories WHERE SprintId = s.Id), 0) as TotalStoryPoints,
                    ISNULL((SELECT SUM(StoryPoints) FROM UserStories WHERE SprintId = s.Id AND Status = 'Done'), 0) as CompletedStoryPoints,
                    (SELECT COUNT(*) FROM Tasks t JOIN UserStories us ON t.StoryId = us.Id WHERE us.SprintId = s.Id) as TotalTasks,
                    (SELECT COUNT(*) FROM Tasks t JOIN UserStories us ON t.StoryId = us.Id WHERE us.SprintId = s.Id AND t.Status = 'Done') as CompletedTasks
                FROM Sprints s
                ORDER BY s.StartDate DESC";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                sprints.Add(new SprintDto
                {
                    Id = (Guid)reader["Id"],
                    ProjectId = (Guid)reader["ProjectId"],
                    Name = reader["Name"].ToString()!,
                    Goal = reader["Goal"]?.ToString(),
                    StartDate = (DateTime)reader["StartDate"],
                    EndDate = (DateTime)reader["EndDate"],
                    Status = reader["Status"].ToString()!,
                    CreatedAt = (DateTime)reader["CreatedAt"],
                    TotalStoryPoints = (int)reader["TotalStoryPoints"],
                    CompletedStoryPoints = (int)reader["CompletedStoryPoints"],
                    TotalTasks = (int)reader["TotalTasks"],
                    CompletedTasks = (int)reader["CompletedTasks"]
                });
            }

            return Results.Ok(sprints);
        });

        // Create sprint
        group.MapPost("/", async (CreateSprintRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var id = Guid.NewGuid();

                var sql = @"
                    INSERT INTO Sprints (Id, ProjectId, Name, Goal, StartDate, EndDate)
                    VALUES (@Id, @ProjectId, @Name, @Goal, @StartDate, @EndDate)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@ProjectId", request.ProjectId);
                cmd.Parameters.AddWithValue("@Name", request.Name);
                cmd.Parameters.AddWithValue("@Goal", (object?)request.Goal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@StartDate", request.StartDate);
                cmd.Parameters.AddWithValue("@EndDate", request.EndDate);

                await cmd.ExecuteNonQueryAsync();

                return Results.Created($"/api/sprints/{id}", new Sprint
                {
                    Id = id,
                    ProjectId = request.ProjectId,
                    Name = request.Name,
                    Goal = request.Goal,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    Status = SprintStatus.Planning,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating sprint: {ex.Message}");
            }
        });

        // Get sprints by project
        group.MapGet("/project/{projectId:guid}", async (Guid projectId, DatabaseContext db) =>
        {
            var sprints = new List<SprintDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT s.*,
                    ISNULL((SELECT SUM(StoryPoints) FROM UserStories WHERE SprintId = s.Id), 0) as TotalStoryPoints,
                    ISNULL((SELECT SUM(StoryPoints) FROM UserStories WHERE SprintId = s.Id AND Status = 'Done'), 0) as CompletedStoryPoints,
                    (SELECT COUNT(*) FROM Tasks t JOIN UserStories us ON t.StoryId = us.Id WHERE us.SprintId = s.Id) as TotalTasks,
                    (SELECT COUNT(*) FROM Tasks t JOIN UserStories us ON t.StoryId = us.Id WHERE us.SprintId = s.Id AND t.Status = 'Done') as CompletedTasks
                FROM Sprints s
                WHERE s.ProjectId = @ProjectId
                ORDER BY s.StartDate DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ProjectId", projectId);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                sprints.Add(new SprintDto
                {
                    Id = (Guid)reader["Id"],
                    ProjectId = (Guid)reader["ProjectId"],
                    Name = reader["Name"].ToString()!,
                    Goal = reader["Goal"]?.ToString(),
                    StartDate = (DateTime)reader["StartDate"],
                    EndDate = (DateTime)reader["EndDate"],
                    Status = reader["Status"].ToString()!,
                    CreatedAt = (DateTime)reader["CreatedAt"],
                    TotalStoryPoints = (int)reader["TotalStoryPoints"],
                    CompletedStoryPoints = (int)reader["CompletedStoryPoints"],
                    TotalTasks = (int)reader["TotalTasks"],
                    CompletedTasks = (int)reader["CompletedTasks"]
                });
            }

            return Results.Ok(sprints);
        });

        // Get sprint by ID
        group.MapGet("/{id:guid}", async (Guid id, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = "SELECT * FROM Sprints WHERE Id = @Id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.NotFound();
            }

            return Results.Ok(MapSprint(reader));
        });

        // Update sprint status
        group.MapPatch("/{id:guid}/status", async (Guid id, UpdateStatusRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = "UPDATE Sprints SET Status = @Status WHERE Id = @Id";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Status", request.Status);
                cmd.Parameters.AddWithValue("@Id", id);

                await cmd.ExecuteNonQueryAsync();
                return Results.Ok(new { message = "Status updated" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error updating status: {ex.Message}");
            }
        });

        // Get burndown data (calculated dynamically)
        group.MapGet("/{id:guid}/burndown", async (Guid id, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            // Get sprint info
            var sprintSql = "SELECT * FROM Sprints WHERE Id = @Id";
            using var sprintCmd = new SqlCommand(sprintSql, conn);
            sprintCmd.Parameters.AddWithValue("@Id", id);
            using var sprintReader = await sprintCmd.ExecuteReaderAsync();

            if (!await sprintReader.ReadAsync())
            {
                return Results.NotFound();
            }

            var sprint = MapSprint(sprintReader);
            sprintReader.Close();

            // Calculate total story points for the sprint
            var totalPointsSql = @"
                SELECT ISNULL(SUM(StoryPoints), 0) as TotalPoints
                FROM UserStories 
                WHERE SprintId = @SprintId";
            
            using var pointsCmd = new SqlCommand(totalPointsSql, conn);
            pointsCmd.Parameters.AddWithValue("@SprintId", id);
            var totalPoints = (int)(await pointsCmd.ExecuteScalarAsync() ?? 0);

            // Calculate days in sprint
            var totalDays = (int)(sprint.EndDate - sprint.StartDate).TotalDays + 1;
            var daysPassed = Math.Min((int)(DateTime.UtcNow - sprint.StartDate).TotalDays + 1, totalDays);
            
            // Generate labels (Day 1, Day 2, etc.)
            var labels = new List<string>();
            for (int i = 1; i <= totalDays; i++)
            {
                labels.Add($"Day {i}");
            }

            // Generate ideal burndown line
            var ideal = new List<int>();
            for (int i = 0; i <= totalDays; i++)
            {
                ideal.Add((int)(totalPoints * (1 - (double)i / totalDays)));
            }

            // Calculate actual burndown based on completed tasks
            var actual = new List<int>();
            var completedPointsSql = @"
                SELECT 
                    DATEDIFF(day, s.StartDate, t.CompletedAt) as DayIndex,
                    ISNULL(SUM(us.StoryPoints), 0) as Points
                FROM Tasks t
                JOIN UserStories us ON t.StoryId = us.Id
                JOIN Sprints s ON us.SprintId = s.Id
                WHERE us.SprintId = @SprintId 
                    AND t.Status = 'Done'
                    AND t.CompletedAt IS NOT NULL
                GROUP BY DATEDIFF(day, s.StartDate, t.CompletedAt)
                ORDER BY DayIndex";
            
            using var completedCmd = new SqlCommand(completedPointsSql, conn);
            completedCmd.Parameters.AddWithValue("@SprintId", id);
            using var completedReader = await completedCmd.ExecuteReaderAsync();
            
            var completedByDay = new Dictionary<int, int>();
            while (await completedReader.ReadAsync())
            {
                var dayIndex = (int)completedReader["DayIndex"];
                var points = (int)completedReader["Points"];
                completedByDay[dayIndex] = points;
            }
            completedReader.Close();

            // Build actual burndown line
            var currentRemaining = totalPoints;
            for (int i = 0; i <= totalDays; i++)
            {
                if (completedByDay.ContainsKey(i))
                {
                    currentRemaining -= completedByDay[i];
                }
                actual.Add(Math.Max(0, currentRemaining));
            }

            return Results.Ok(new { 
                labels, 
                ideal, 
                actual,
                totalPoints,
                sprintName = sprint.Name
            });
        });
    }

    private static Sprint MapSprint(SqlDataReader reader)
    {
        return new Sprint
        {
            Id = (Guid)reader["Id"],
            ProjectId = (Guid)reader["ProjectId"],
            Name = reader["Name"].ToString()!,
            Goal = reader["Goal"]?.ToString(),
            StartDate = (DateTime)reader["StartDate"],
            EndDate = (DateTime)reader["EndDate"],
            Status = Enum.Parse<SprintStatus>(reader["Status"].ToString()!),
            CreatedAt = (DateTime)reader["CreatedAt"]
        };
    }
}
