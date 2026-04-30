using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class StoryRoutes
{
    public static void MapStoryRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stories");

        // Create story
        group.MapPost("/", async (CreateStoryRequest request, DatabaseContext db) =>
        {
            try
            {
                Console.WriteLine($"[CREATE STORY] ProjectId: {request.ProjectId}, Title: {request.Title}, SprintId: {request.SprintId}");
                
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var id = Guid.NewGuid();
                var status = request.SprintId.HasValue ? StoryStatus.SprintBacklog : StoryStatus.Backlog;
                Console.WriteLine($"[CREATE STORY] Generated Id: {id}, Status: {status}");

                var sql = @"
                    INSERT INTO UserStories (Id, ProjectId, SprintId, Title, Description, AcceptanceCriteria, StoryPoints, Priority, Status, CreatedBy)
                    VALUES (@Id, @ProjectId, @SprintId, @Title, @Description, @AcceptanceCriteria, @StoryPoints, @Priority, @Status, @CreatedBy)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@ProjectId", request.ProjectId);
                cmd.Parameters.AddWithValue("@SprintId", (object?)request.SprintId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Title", request.Title);
                cmd.Parameters.AddWithValue("@Description", (object?)request.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AcceptanceCriteria", (object?)request.AcceptanceCriteria ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@StoryPoints", (object?)request.StoryPoints ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Priority", request.Priority);
                cmd.Parameters.AddWithValue("@Status", status.ToString());
                cmd.Parameters.AddWithValue("@CreatedBy", DBNull.Value);

                var affected = await cmd.ExecuteNonQueryAsync();
                Console.WriteLine($"[CREATE STORY] Rows affected: {affected}");

                return Results.Created($"/api/stories/{id}", new UserStory
                {
                    Id = id,
                    ProjectId = request.ProjectId,
                    SprintId = request.SprintId,
                    Title = request.Title,
                    Description = request.Description,
                    AcceptanceCriteria = request.AcceptanceCriteria,
                    StoryPoints = request.StoryPoints,
                    Priority = request.Priority,
                    Status = status,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating story: {ex.Message}");
            }
        });

        // Get product backlog (stories without sprint)
        group.MapGet("/project/{projectId:guid}/backlog", async (Guid projectId, DatabaseContext db) =>
        {
            Console.WriteLine($"[GET BACKLOG] Requested for project: {projectId}");
            var stories = new List<UserStoryDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();
            
            // First, let's check all stories in the database for this project
            var countSql = "SELECT COUNT(*) FROM UserStories WHERE ProjectId = @ProjectId";
            using var countCmd = new SqlCommand(countSql, conn);
            countCmd.Parameters.AddWithValue("@ProjectId", projectId);
            var totalCount = (int)(await countCmd.ExecuteScalarAsync() ?? 0);
            Console.WriteLine($"[GET BACKLOG] Total stories for project in DB: {totalCount}");

            var sql = @"
                SELECT us.*,
                    (SELECT COUNT(*) FROM Tasks WHERE StoryId = us.Id) as TaskCount,
                    (SELECT COUNT(*) FROM Tasks WHERE StoryId = us.Id AND Status = 'Done') as CompletedTaskCount
                FROM UserStories us
                WHERE us.ProjectId = @ProjectId AND us.Status = 'Backlog'
                ORDER BY us.Priority DESC, us.CreatedAt DESC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ProjectId", projectId);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                stories.Add(MapUserStoryDto(reader));
            }
            
            Console.WriteLine($"[GET BACKLOG] Returning {stories.Count} stories");
            return Results.Ok(stories);
        });

        // Get sprint backlog
        group.MapGet("/sprint/{sprintId:guid}", async (Guid sprintId, DatabaseContext db) =>
        {
            try
            {
                var stories = new List<UserStoryDto>();

                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                Console.WriteLine($"[SPRINT BACKLOG] Loading stories for sprint {sprintId}");

                var sql = @"
                    SELECT us.Id, us.ProjectId, us.SprintId, us.Title, us.Description, 
                           us.AcceptanceCriteria, us.StoryPoints, us.Priority, us.Status, 
                           us.CreatedBy, us.CreatedAt,
                           (SELECT COUNT(*) FROM Tasks WHERE StoryId = us.Id) as TaskCount,
                           (SELECT COUNT(*) FROM Tasks WHERE StoryId = us.Id AND Status = 'Done') as CompletedTaskCount
                    FROM UserStories us
                    WHERE us.SprintId = @SprintId
                    ORDER BY us.Priority DESC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SprintId", sprintId);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    stories.Add(new UserStoryDto
                    {
                        Id = (Guid)reader["Id"],
                        ProjectId = (Guid)reader["ProjectId"],
                        SprintId = reader["SprintId"] as Guid?,
                        Title = reader["Title"].ToString()!,
                        Description = reader["Description"]?.ToString(),
                        AcceptanceCriteria = reader["AcceptanceCriteria"]?.ToString(),
                        StoryPoints = reader["StoryPoints"] as int?,
                        Priority = (int)reader["Priority"],
                        Status = Enum.Parse<StoryStatus>(reader["Status"].ToString()!),
                        TaskCount = (int)reader["TaskCount"],
                        CompletedTaskCount = (int)reader["CompletedTaskCount"]
                    });
                }

                Console.WriteLine($"[SPRINT BACKLOG] Found {stories.Count} stories");
                return Results.Ok(stories);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SPRINT BACKLOG] Error: {ex.Message}");
                return Results.Problem($"Error loading sprint backlog: {ex.Message}");
            }
        });

        // Move story to sprint
        group.MapPost("/{id:guid}/move-to-sprint", async (Guid id, Guid sprintId, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = @"
                    UPDATE UserStories 
                    SET SprintId = @SprintId, Status = @Status 
                    WHERE Id = @Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SprintId", sprintId);
                cmd.Parameters.AddWithValue("@Status", StoryStatus.SprintBacklog.ToString());
                cmd.Parameters.AddWithValue("@Id", id);

                await cmd.ExecuteNonQueryAsync();
                return Results.Ok(new { message = "Story moved to sprint" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error moving story: {ex.Message}");
            }
        });

        // Update story status
        group.MapPatch("/{id:guid}/status", async (Guid id, UpdateStatusRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = "UPDATE UserStories SET Status = @Status WHERE Id = @Id";
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

        // Get story by ID
        group.MapGet("/{id:guid}", async (Guid id, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT us.*,
                    (SELECT COUNT(*) FROM Tasks WHERE StoryId = us.Id) as TaskCount,
                    (SELECT COUNT(*) FROM Tasks WHERE StoryId = us.Id AND Status = 'Done') as CompletedTaskCount
                FROM UserStories us
                WHERE us.Id = @Id";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.NotFound();
            }

            return Results.Ok(MapUserStoryDto(reader));
        });
        // Update story
        group.MapPut("/{id:guid}", async (Guid id, CreateStoryRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var status = request.SprintId.HasValue ? StoryStatus.SprintBacklog : StoryStatus.Backlog;

                var sql = @"
                    UPDATE UserStories 
                    SET ProjectId = @ProjectId, 
                        SprintId = @SprintId, 
                        Title = @Title, 
                        Description = @Description, 
                        AcceptanceCriteria = @AcceptanceCriteria, 
                        StoryPoints = @StoryPoints, 
                        Priority = @Priority, 
                        Status = @Status
                    WHERE Id = @Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@ProjectId", request.ProjectId);
                cmd.Parameters.AddWithValue("@SprintId", (object?)request.SprintId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Title", request.Title);
                cmd.Parameters.AddWithValue("@Description", (object?)request.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AcceptanceCriteria", (object?)request.AcceptanceCriteria ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@StoryPoints", (object?)request.StoryPoints ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Priority", request.Priority);
                cmd.Parameters.AddWithValue("@Status", status.ToString());

                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected == 0) return Results.NotFound();

                return Results.Ok(new { message = "Historia actualizada exitosamente" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error updating story: {ex.Message}");
            }
        });

        // Delete story
        group.MapDelete("/{id:guid}", async (Guid id, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Delete related tasks first
                var sqlTasks = "DELETE FROM Tasks WHERE StoryId = @Id";
                using (var cmdTasks = new SqlCommand(sqlTasks, conn))
                {
                    cmdTasks.Parameters.AddWithValue("@Id", id);
                    await cmdTasks.ExecuteNonQueryAsync();
                }

                var sqlStory = "DELETE FROM UserStories WHERE Id = @Id";
                using (var cmdStory = new SqlCommand(sqlStory, conn))
                {
                    cmdStory.Parameters.AddWithValue("@Id", id);
                    var affected = await cmdStory.ExecuteNonQueryAsync();
                    if (affected == 0) return Results.NotFound();
                }

                return Results.Ok(new { message = "Historia eliminada exitosamente" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error deleting story: {ex.Message}");
            }
        });

        // Get all stories for board (Kanban view)
        group.MapGet("/project/{projectId:guid}/board", async (Guid projectId, DatabaseContext db) =>
        {
            try
            {
                Console.WriteLine($"[BOARD] Loading board for project: {projectId}");
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Get stories
                var stories = new List<BoardStoryDto>();
                var storiesSql = @"
                    SELECT us.Id, us.ProjectId, us.Title, us.Description, 
                           us.StoryPoints, us.Priority, us.Status,
                           us.CreatedBy as AssigneeId,
                    u.Name as AssigneeName
                    FROM UserStories us
                    LEFT JOIN Users u ON us.CreatedBy = u.Id
                    WHERE us.ProjectId = @ProjectId
                    ORDER BY us.Priority DESC, us.CreatedAt DESC";

                using (var cmd = new SqlCommand(storiesSql, conn))
                {
                    cmd.Parameters.AddWithValue("@ProjectId", projectId);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        stories.Add(new BoardStoryDto
                        {
                            Id = (Guid)reader["Id"],
                            ProjectId = (Guid)reader["ProjectId"],
                            Title = reader["Title"].ToString()!,
                            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader["Description"].ToString(),
                            StoryPoints = reader.IsDBNull(reader.GetOrdinal("StoryPoints")) ? null : (int)reader["StoryPoints"],
                            Priority = (int)reader["Priority"],
                            Status = reader["Status"].ToString()!,
                            AssigneeId = reader.IsDBNull(reader.GetOrdinal("AssigneeId")) ? null : (Guid)reader["AssigneeId"],
                            AssigneeName = reader.IsDBNull(reader.GetOrdinal("AssigneeName")) ? null : reader["AssigneeName"].ToString()
                        });
                    }
                }
                Console.WriteLine($"[BOARD] Loaded {stories.Count} stories");

                // Get project members
                var members = new List<ProjectMemberDto>();
                var membersSql = @"
                    SELECT DISTINCT u.Id, u.Name, u.Email, u.Role
                    FROM Users u
                    WHERE u.Id IN (
                        SELECT ProductOwnerId FROM Projects WHERE Id = @ProjectId AND ProductOwnerId IS NOT NULL
                        UNION
                        SELECT ScrumMasterId FROM Projects WHERE Id = @ProjectId AND ScrumMasterId IS NOT NULL
                        UNION  
                        SELECT CreatedBy FROM UserStories WHERE ProjectId = @ProjectId AND CreatedBy IS NOT NULL
                    )";

                using (var cmd = new SqlCommand(membersSql, conn))
                {
                    cmd.Parameters.AddWithValue("@ProjectId", projectId);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        members.Add(new ProjectMemberDto
                        {
                            Id = (Guid)reader["Id"],
                            Name = reader["Name"].ToString()!,
                            Email = reader["Email"].ToString()!,
                            Role = reader["Role"].ToString()!
                        });
                    }
                }
                Console.WriteLine($"[BOARD] Loaded {members.Count} members");
                foreach (var m in members) {
                    Console.WriteLine($"[BOARD] Member: {m.Name} ({m.Role})");
                }

                return Results.Ok(new BoardDataDto 
                { 
                    Stories = stories, 
                    Members = members 
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BOARD] Error: {ex.Message}");
                return Results.Problem($"Error loading board: {ex.Message}");
            }
        });

        // Update story status (for drag & drop)
        group.MapPut("/{id:guid}/status", async (Guid id, UpdateStatusRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = "UPDATE UserStories SET Status = @Status WHERE Id = @Id";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Status", request.Status);
                cmd.Parameters.AddWithValue("@Id", id);

                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected == 0) return Results.NotFound();

                return Results.Ok(new { message = "Status actualizado" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error updating status: {ex.Message}");
            }
        });
    }

    private static UserStoryDto MapUserStoryDto(SqlDataReader reader)
    {
        return new UserStoryDto
        {
            Id = (Guid)reader["Id"],
            ProjectId = (Guid)reader["ProjectId"],
            SprintId = reader.IsDBNull(reader.GetOrdinal("SprintId")) ? null : (Guid)reader["SprintId"],
            Title = reader["Title"].ToString()!,
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader["Description"].ToString(),
            AcceptanceCriteria = reader.IsDBNull(reader.GetOrdinal("AcceptanceCriteria")) ? null : reader["AcceptanceCriteria"].ToString(),
            StoryPoints = reader.IsDBNull(reader.GetOrdinal("StoryPoints")) ? null : (int)reader["StoryPoints"],
            Priority = (int)reader["Priority"],
            Status = Enum.Parse<StoryStatus>(reader["Status"].ToString()!),
            CreatedBy = reader.IsDBNull(reader.GetOrdinal("CreatedBy")) ? null : (Guid)reader["CreatedBy"],
            CreatedAt = (DateTime)reader["CreatedAt"],
            TaskCount = reader.GetColumnSchema().Any(c => c.ColumnName == "TaskCount") ? (int)reader["TaskCount"] : 0,
            CompletedTaskCount = reader.GetColumnSchema().Any(c => c.ColumnName == "CompletedTaskCount") ? (int)reader["CompletedTaskCount"] : 0,
            Tasks = new List<TaskItemDto>()
        };
    }

    private static TaskItemDto MapTaskItemDto(SqlDataReader reader)
    {
        return new TaskItemDto
        {
            Id = (Guid)reader["Id"],
            StoryId = (Guid)reader["StoryId"],
            Title = reader["Title"].ToString()!,
            Description = reader["Description"]?.ToString(),
            EstimatedHours = reader["EstimatedHours"] as int?,
            ActualHours = (int)(reader["ActualHours"] ?? 0),
            Status = Enum.Parse<Models.TaskStatus>(reader["Status"].ToString()),
            AssignedTo = reader["AssignedTo"] as Guid?,
            AssignedToName = reader["AssignedToName"]?.ToString(),
            CreatedAt = (DateTime)reader["CreatedAt"],
            CompletedAt = reader["CompletedAt"] as DateTime?
        };
    }
}
