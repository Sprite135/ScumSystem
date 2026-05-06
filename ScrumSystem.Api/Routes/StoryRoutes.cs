using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class StoryRoutes
{
    public static void MapStoryRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stories");

        group.MapGet("/", async (DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(us.Id AS NVARCHAR(36)), CAST(us.ProjectId AS NVARCHAR(36)) as ProjectId, CAST(us.SprintId AS NVARCHAR(36)) as SprintId,
                       us.Title, us.Description, us.AcceptanceCriteria, us.StoryPoints, us.Priority, us.Status, us.[Key],
                       CAST(us.AssigneeId AS NVARCHAR(36)) as AssigneeId, u.Name as AssigneeName, us.CreatedAt
                FROM UserStories us
                LEFT JOIN Users u ON us.AssigneeId = u.Id
                ORDER BY us.CreatedAt DESC";

            var stories = new List<UserStoryDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var priorityText = reader.IsDBNull(7) ? "Medium" : reader.GetInt32(7) switch { 1 => "Low", 3 => "High", _ => "Medium" };
                    stories.Add(new UserStoryDto
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        SprintId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Title = reader.GetString(3),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        AcceptanceCriteria = reader.IsDBNull(5) ? null : reader.GetString(5),
                        StoryPoints = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        Priority = priorityText,
                        Status = reader.GetString(8),
                        Key = reader.IsDBNull(9) ? null : reader.GetString(9),
                        AssigneeId = reader.IsDBNull(10) ? null : reader.GetString(10),
                        AssigneeName = reader.IsDBNull(11) ? null : reader.GetString(11),
                        CreatedAt = reader.GetDateTime(12)
                    });
                }
            }

            return Results.Ok(stories);
        });

        group.MapGet("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(us.Id AS NVARCHAR(36)), CAST(us.ProjectId AS NVARCHAR(36)) as ProjectId, CAST(us.SprintId AS NVARCHAR(36)) as SprintId,
                       us.Title, us.Description, us.AcceptanceCriteria, us.StoryPoints, us.Priority, us.Status, us.[Key],
                       CAST(us.AssigneeId AS NVARCHAR(36)) as AssigneeId, u.Name as AssigneeName, us.CreatedAt
                FROM UserStories us
                LEFT JOIN Users u ON us.AssigneeId = u.Id
                WHERE CAST(us.Id AS NVARCHAR(36)) = @Id";

            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var storyDto = new UserStoryDto
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        SprintId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Title = reader.GetString(3),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        AcceptanceCriteria = reader.IsDBNull(5) ? null : reader.GetString(5),
                        StoryPoints = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        Priority = reader.IsDBNull(7) ? "Medium" : reader.GetString(7),
                        Status = reader.GetString(8),
                        Key = reader.IsDBNull(9) ? null : reader.GetString(9),
                        AssigneeId = reader.IsDBNull(10) ? null : reader.GetString(10),
                        AssigneeName = reader.IsDBNull(11) ? null : reader.GetString(11),
                        CreatedAt = reader.GetDateTime(12)
                    };
                    
                    // Load tasks for this story
                    reader.Close(); // Close first reader before using second one
                    
                    var tasksSql = @"
                        SELECT t.Id, t.StoryId, t.Title, t.Description, t.EstimatedHours, t.ActualHours, 
                               t.Status, t.Priority, t.AssignedToId, u.Name as AssignedToName, t.CreatedAt
                        FROM Tasks t
                        LEFT JOIN Users u ON t.AssignedToId = u.Id
                        WHERE t.StoryId = @StoryId
                        ORDER BY t.CreatedAt";
                    
                    using (var tasksCmd = new SqlCommand(tasksSql, connection))
                    {
                        tasksCmd.Parameters.AddWithValue("@StoryId", id);
                        using var tasksReader = await tasksCmd.ExecuteReaderAsync();
                        while (await tasksReader.ReadAsync())
                        {
                            storyDto.Tasks.Add(new TaskItemDto
                            {
                                Id = tasksReader.GetGuid(0).ToString(),
                                StoryId = tasksReader.GetGuid(1).ToString(),
                                Title = tasksReader.GetString(2),
                                Description = tasksReader.IsDBNull(3) ? null : tasksReader.GetString(3),
                                EstimatedHours = tasksReader.IsDBNull(4) ? null : tasksReader.GetInt32(4),
                                ActualHours = tasksReader.IsDBNull(5) ? null : tasksReader.GetInt32(5),
                                Status = tasksReader.GetString(6),
                                Priority = tasksReader.GetInt32(7),
                                AssignedToId = tasksReader.IsDBNull(8) ? null : tasksReader.GetGuid(8).ToString(),
                                AssignedToName = tasksReader.IsDBNull(9) ? null : tasksReader.GetString(9),
                                CreatedAt = tasksReader.GetDateTime(10)
                            });
                        }
                    }
                    
                    return Results.Ok(storyDto);
                }
            }

            return Results.NotFound();
        });

        group.MapGet("/project/{projectId}", async (string projectId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(us.Id AS NVARCHAR(36)), CAST(us.ProjectId AS NVARCHAR(36)) as ProjectId, CAST(us.SprintId AS NVARCHAR(36)) as SprintId,
                       us.Title, us.Description, us.AcceptanceCriteria, us.StoryPoints, us.Priority, us.Status, us.[Key],
                       CAST(us.AssigneeId AS NVARCHAR(36)) as AssigneeId, u.Name as AssigneeName, us.CreatedAt
                FROM UserStories us
                LEFT JOIN Users u ON us.AssigneeId = u.Id
                WHERE CAST(us.ProjectId AS NVARCHAR(36)) = @ProjectId
                ORDER BY us.CreatedAt DESC";

            var stories = new List<UserStoryDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@ProjectId", projectId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    stories.Add(new UserStoryDto
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        SprintId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Title = reader.GetString(3),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        AcceptanceCriteria = reader.IsDBNull(5) ? null : reader.GetString(5),
                        StoryPoints = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        Priority = reader.IsDBNull(7) ? "Medium" : reader.GetString(7),
                        Status = reader.GetString(8),
                        Key = reader.IsDBNull(9) ? null : reader.GetString(9),
                        AssigneeId = reader.IsDBNull(10) ? null : reader.GetString(10),
                        AssigneeName = reader.IsDBNull(11) ? null : reader.GetString(11),
                        CreatedAt = reader.GetDateTime(12)
                    });
                }
            }

            return Results.Ok(stories);
        });

        group.MapGet("/project/{projectId}/backlog", async (string projectId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(us.Id AS NVARCHAR(36)), CAST(us.ProjectId AS NVARCHAR(36)) as ProjectId, CAST(us.SprintId AS NVARCHAR(36)) as SprintId,
                       us.Title, us.Description, us.AcceptanceCriteria, us.StoryPoints, us.Priority, us.Status, us.[Key],
                       CAST(us.AssigneeId AS NVARCHAR(36)) as AssigneeId, u.Name as AssigneeName, us.CreatedAt
                FROM UserStories us
                LEFT JOIN Users u ON us.AssigneeId = u.Id
                WHERE CAST(us.ProjectId AS NVARCHAR(36)) = @ProjectId AND us.SprintId IS NULL
                ORDER BY us.CreatedAt DESC";

            var stories = new List<UserStoryDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@ProjectId", projectId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    stories.Add(new UserStoryDto
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        SprintId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Title = reader.GetString(3),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        AcceptanceCriteria = reader.IsDBNull(5) ? null : reader.GetString(5),
                        StoryPoints = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        Priority = reader.IsDBNull(7) ? "Medium" : reader.GetString(7),
                        Status = reader.GetString(8),
                        Key = reader.IsDBNull(9) ? null : reader.GetString(9),
                        AssigneeId = reader.IsDBNull(10) ? null : reader.GetString(10),
                        AssigneeName = reader.IsDBNull(11) ? null : reader.GetString(11),
                        CreatedAt = reader.GetDateTime(12)
                    });
                }
            }

            return Results.Ok(stories);
        });

        group.MapGet("/project/{projectId}/board", async (string projectId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Get project members
            var members = new List<ProjectMemberDto>();
            using (var membersCmd = new SqlCommand(@"
                SELECT CAST(u.Id AS NVARCHAR(36)), u.Name, u.Email, pm.Role
                FROM ProjectMembers pm
                INNER JOIN Users u ON pm.UserId = u.Id
                WHERE CAST(pm.ProjectId AS NVARCHAR(36)) = @ProjectId
                ORDER BY u.Name", connection))
            {
                membersCmd.Parameters.AddWithValue("@ProjectId", projectId);
                using var reader = await membersCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    members.Add(new ProjectMemberDto
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Email = reader.GetString(2),
                        Role = reader.IsDBNull(3) ? "Developer" : reader.GetString(3)
                    });
                }
            }

            // Check for active sprints
            var hasActiveSprint = false;
            using (var sprintCmd = new SqlCommand(@"
                SELECT COUNT(*) FROM Sprints 
                WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId AND Status = 'Active'", connection))
            {
                sprintCmd.Parameters.AddWithValue("@ProjectId", projectId);
                hasActiveSprint = (int)await sprintCmd.ExecuteScalarAsync() > 0;
            }

            // Get stories from active sprints
            var stories = new List<BoardStoryDto>();
            var sql = @"
                SELECT CAST(us.Id AS NVARCHAR(36)), CAST(us.ProjectId AS NVARCHAR(36)), CAST(us.SprintId AS NVARCHAR(36)),
                       us.Title, us.Description, us.StoryPoints, us.Priority, us.Status,
                       CAST(us.AssigneeId AS NVARCHAR(36)), u.Name as AssigneeName
                FROM UserStories us
                LEFT JOIN Users u ON us.AssigneeId = u.Id
                INNER JOIN Sprints s ON us.SprintId = s.Id
                WHERE CAST(us.ProjectId AS NVARCHAR(36)) = @ProjectId 
                  AND s.Status = 'Active'
                ORDER BY us.CreatedAt DESC";

            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@ProjectId", projectId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    stories.Add(new BoardStoryDto
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        SprintId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Title = reader.GetString(3),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        StoryPoints = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        Priority = reader.IsDBNull(6) ? "Medium" : reader.GetString(6),
                        Status = reader.GetString(7),
                        AssigneeId = reader.IsDBNull(8) ? null : reader.GetString(8),
                        AssigneeName = reader.IsDBNull(9) ? null : reader.GetString(9)
                    });
                }
            }

            return Results.Ok(new BoardDataDto
            {
                Stories = stories,
                Members = members,
                HasActiveSprint = hasActiveSprint
            });
        });

        group.MapGet("/sprint/{sprintId}", async (string sprintId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(us.Id AS NVARCHAR(36)), CAST(us.ProjectId AS NVARCHAR(36)) as ProjectId, CAST(us.SprintId AS NVARCHAR(36)) as SprintId,
                       us.Title, us.Description, us.AcceptanceCriteria, us.StoryPoints, us.Priority, us.Status, us.[Key],
                       CAST(us.AssigneeId AS NVARCHAR(36)) as AssigneeId, u.Name as AssigneeName, us.CreatedAt
                FROM UserStories us
                LEFT JOIN Users u ON us.AssigneeId = u.Id
                WHERE CAST(us.SprintId AS NVARCHAR(36)) = @SprintId
                ORDER BY us.CreatedAt DESC";

            var stories = new List<UserStoryDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@SprintId", sprintId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    stories.Add(new UserStoryDto
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        SprintId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Title = reader.GetString(3),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        AcceptanceCriteria = reader.IsDBNull(5) ? null : reader.GetString(5),
                        StoryPoints = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        Priority = reader.IsDBNull(7) ? "Medium" : reader.GetString(7),
                        Status = reader.GetString(8),
                        Key = reader.IsDBNull(9) ? null : reader.GetString(9),
                        AssigneeId = reader.IsDBNull(10) ? null : reader.GetString(10),
                        AssigneeName = reader.IsDBNull(11) ? null : reader.GetString(11),
                        CreatedAt = reader.GetDateTime(12)
                    });
                }
            }

            return Results.Ok(stories);
        });

        group.MapPost("/", async (CreateStoryRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verify project exists in SQL
            string projectKey;
            using (var checkCmd = new SqlCommand("SELECT [Key] FROM Projects WHERE CAST(Id AS NVARCHAR(36)) = @ProjectId", connection))
            {
                checkCmd.Parameters.AddWithValue("@ProjectId", request.ProjectId);
                var result = await checkCmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                {
                    return Results.BadRequest("El proyecto no existe");
                }
                projectKey = result?.ToString() ?? "PROJ";
            }

            // Get story count for this project
            int storyNumber;
            using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM UserStories WHERE CAST(ProjectId AS NVARCHAR(36)) = @ProjectId", connection))
            {
                countCmd.Parameters.AddWithValue("@ProjectId", request.ProjectId);
                storyNumber = (int)await countCmd.ExecuteScalarAsync() + 1;
            }

            var storyId = Guid.NewGuid();
            var storyKey = $"{projectKey}-{storyNumber}";
            var createdAt = DateTime.UtcNow;
            var status = string.IsNullOrWhiteSpace(request.Status)
                ? (string.IsNullOrWhiteSpace(request.SprintId) ? "Backlog" : "SprintBacklog")
                : request.Status;
            var priorityValue = request.PriorityValue;
            var priorityText = priorityValue switch
            {
                1 => "Low",
                3 => "High",
                _ => "Medium"
            };

            // Insert story
            var insertSql = @"
                INSERT INTO UserStories (Id, ProjectId, SprintId, Title, Description, AcceptanceCriteria, StoryPoints, Priority, AssigneeId, Status, [Key], CreatedAt) 
                VALUES (@Id, @ProjectId, @SprintId, @Title, @Description, @AcceptanceCriteria, @StoryPoints, @Priority, @AssigneeId, @Status, @Key, @CreatedAt)";
            
            using (var insertCmd = new SqlCommand(insertSql, connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", storyId);
                insertCmd.Parameters.AddWithValue("@ProjectId", Guid.Parse(request.ProjectId));
                insertCmd.Parameters.AddWithValue("@SprintId", string.IsNullOrWhiteSpace(request.SprintId) ? DBNull.Value : Guid.Parse(request.SprintId));
                insertCmd.Parameters.AddWithValue("@Title", request.Title.Trim());
                insertCmd.Parameters.AddWithValue("@Description", (object?)request.Description?.Trim() ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@AcceptanceCriteria", (object?)request.AcceptanceCriteria?.Trim() ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@StoryPoints", (object?)request.StoryPoints ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@Priority", priorityValue);
                insertCmd.Parameters.AddWithValue("@AssigneeId", string.IsNullOrWhiteSpace(request.AssigneeId) ? DBNull.Value : Guid.Parse(request.AssigneeId));
                insertCmd.Parameters.AddWithValue("@Status", status);
                insertCmd.Parameters.AddWithValue("@Key", storyKey);
                insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync();
            }

            var storyDto = new UserStoryDto
            {
                Id = storyId.ToString(),
                ProjectId = request.ProjectId,
                SprintId = request.SprintId,
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                AcceptanceCriteria = request.AcceptanceCriteria?.Trim(),
                StoryPoints = request.StoryPoints,
                Priority = priorityText,
                AssigneeId = request.AssigneeId,
                Status = status,
                Key = storyKey,
                CreatedAt = createdAt
            };

            return Results.Created($"/api/stories/{storyId}", storyDto);
        });

        group.MapPut("/{id}", async (string id, CreateStoryRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if story exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM UserStories WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var count = await checkCmd.ExecuteScalarAsync();
                if (count == null || (int)count == 0)
                {
                    return Results.NotFound();
                }
            }

            var status = string.IsNullOrWhiteSpace(request.Status)
                ? (string.IsNullOrWhiteSpace(request.SprintId) ? "Backlog" : "SprintBacklog")
                : request.Status;

            // Priority as int for database (1=Low, 2=Medium, 3=High)
            var priorityValue = request.PriorityValue;

            var updateSql = @"
                UPDATE UserStories 
                SET Title = @Title, Description = @Description, AcceptanceCriteria = @AcceptanceCriteria,
                    StoryPoints = @StoryPoints, Priority = @Priority, SprintId = @SprintId, 
                    AssigneeId = @AssigneeId, Status = @Status
                WHERE CAST(Id AS NVARCHAR(36)) = @Id";

            using (var updateCmd = new SqlCommand(updateSql, connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@Title", request.Title.Trim());
                updateCmd.Parameters.AddWithValue("@Description", (object?)request.Description?.Trim() ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@AcceptanceCriteria", (object?)request.AcceptanceCriteria?.Trim() ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@StoryPoints", (object?)request.StoryPoints ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Priority", priorityValue);
                updateCmd.Parameters.AddWithValue("@SprintId", string.IsNullOrWhiteSpace(request.SprintId) ? DBNull.Value : Guid.Parse(request.SprintId));
                updateCmd.Parameters.AddWithValue("@AssigneeId", string.IsNullOrWhiteSpace(request.AssigneeId) ? DBNull.Value : Guid.Parse(request.AssigneeId));
                updateCmd.Parameters.AddWithValue("@Status", status);
                await updateCmd.ExecuteNonQueryAsync();
            }

            // Get updated story to return
            var selectSql = @"
                SELECT CAST(us.Id AS NVARCHAR(36)), CAST(us.ProjectId AS NVARCHAR(36)) as ProjectId, CAST(us.SprintId AS NVARCHAR(36)) as SprintId,
                       us.Title, us.Description, us.AcceptanceCriteria, us.StoryPoints, us.Priority, us.Status, us.[Key],
                       CAST(us.AssigneeId AS NVARCHAR(36)) as AssigneeId, u.Name as AssigneeName, us.CreatedAt
                FROM UserStories us
                LEFT JOIN Users u ON us.AssigneeId = u.Id
                WHERE CAST(us.Id AS NVARCHAR(36)) = @Id";

            using (var selectCmd = new SqlCommand(selectSql, connection))
            {
                selectCmd.Parameters.AddWithValue("@Id", id);
                using var reader = await selectCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var priorityText = reader.IsDBNull(7) ? "Medium" : reader.GetString(7);
                    var storyDto = new UserStoryDto
                    {
                        Id = reader.GetString(0),
                        ProjectId = reader.GetString(1),
                        SprintId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Title = reader.GetString(3),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        AcceptanceCriteria = reader.IsDBNull(5) ? null : reader.GetString(5),
                        StoryPoints = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        Priority = priorityText,
                        Status = reader.GetString(8),
                        Key = reader.IsDBNull(9) ? null : reader.GetString(9),
                        AssigneeId = reader.IsDBNull(10) ? null : reader.GetString(10),
                        AssigneeName = reader.IsDBNull(11) ? null : reader.GetString(11),
                        CreatedAt = reader.GetDateTime(12)
                    };
                    return Results.Ok(storyDto);
                }
            }

            return Results.NotFound();
        });

        group.MapPut("/{id}/status", async (string id, UpdateStatusRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var updateSql = "UPDATE UserStories SET Status = @Status WHERE CAST(Id AS NVARCHAR(36)) = @Id";
            using (var cmd = new SqlCommand(updateSql, connection))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Status", request.Status);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                
                if (rowsAffected == 0)
                {
                    return Results.NotFound();
                }
            }

            return Results.Ok(new { message = "Historia actualizada" });
        });

        group.MapPost("/{id}/move-to-sprint", async (string id, string sprintId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if story exists
            using (var checkStoryCmd = new SqlCommand("SELECT COUNT(*) FROM UserStories WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkStoryCmd.Parameters.AddWithValue("@Id", id);
                var storyCount = await checkStoryCmd.ExecuteScalarAsync();
                if (storyCount == null || (int)storyCount == 0)
                {
                    return Results.NotFound();
                }
            }

            // Check if sprint exists
            using (var checkSprintCmd = new SqlCommand("SELECT COUNT(*) FROM Sprints WHERE CAST(Id AS NVARCHAR(36)) = @SprintId", connection))
            {
                checkSprintCmd.Parameters.AddWithValue("@SprintId", sprintId);
                var sprintCount = await checkSprintCmd.ExecuteScalarAsync();
                if (sprintCount == null || (int)sprintCount == 0)
                {
                    return Results.BadRequest("El sprint no existe");
                }
            }

            // Update story to move to sprint
            using (var updateCmd = new SqlCommand(@"
                UPDATE UserStories 
                SET SprintId = @SprintId, Status = @Status
                WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@SprintId", Guid.Parse(sprintId));
                updateCmd.Parameters.AddWithValue("@Status", "Backlog");
                await updateCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Historia movida al sprint" });
        });

        group.MapPost("/{id}/move-to-backlog", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if story exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM UserStories WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var count = await checkCmd.ExecuteScalarAsync();
                if (count == null || (int)count == 0)
                {
                    return Results.NotFound();
                }
            }

            // Update story to move to backlog (remove sprint assignment)
            using (var updateCmd = new SqlCommand(@"
                UPDATE UserStories 
                SET SprintId = @SprintId, Status = @Status
                WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@SprintId", DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Status", "Backlog");
                await updateCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Historia movida al backlog" });
        });

        // Endpoint genérico para mover historias
        group.MapPost("/{id}/move", async (string id, MoveStoryRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Verificar que la historia existe
                using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM UserStories WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@Id", id);
                    var count = await checkCmd.ExecuteScalarAsync();
                    if (count == null || (int)count == 0)
                    {
                        transaction.Rollback();
                        return Results.NotFound(new { message = "Historia no encontrada" });
                    }
                }

                // Actualizar SprintId y Status según el request
                string sql;
                if (request.SprintId == null)
                {
                    // Mover al backlog
                    sql = @"
                        UPDATE UserStories 
                        SET SprintId = NULL, Status = @Status, UpdatedAt = @UpdatedAt
                        WHERE CAST(Id AS NVARCHAR(36)) = @Id";
                }
                else
                {
                    // Mover a sprint específico
                    sql = @"
                        UPDATE UserStories 
                        SET SprintId = @SprintId, Status = @Status, UpdatedAt = @UpdatedAt
                        WHERE CAST(Id AS NVARCHAR(36)) = @Id";
                }

                using (var updateCmd = new SqlCommand(sql, connection, transaction))
                {
                    updateCmd.Parameters.AddWithValue("@Id", id);
                    updateCmd.Parameters.AddWithValue("@Status", request.Status);
                    updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);
                    
                    if (request.SprintId != null)
                    {
                        updateCmd.Parameters.AddWithValue("@SprintId", Guid.Parse(request.SprintId));
                    }
                    
                    await updateCmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                return Results.Ok(new { message = "Historia movida exitosamente" });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Results.Problem($"Error al mover historia: {ex.Message}");
            }
        });

        group.MapPost("/{id}/comments", async (string id, CreateStoryCommentRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if story exists
            using (var checkStoryCmd = new SqlCommand("SELECT COUNT(*) FROM UserStories WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkStoryCmd.Parameters.AddWithValue("@Id", id);
                var storyCount = await checkStoryCmd.ExecuteScalarAsync();
                if (storyCount == null || (int)storyCount == 0)
                {
                    return Results.NotFound();
                }
            }

            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest("El comentario no puede estar vacio.");
            }

            var commentId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;

            // Insert comment
            using (var insertCmd = new SqlCommand(@"
                INSERT INTO StoryComments (Id, StoryId, UserId, Message, CreatedAt) 
                VALUES (@Id, @StoryId, @UserId, @Message, @CreatedAt)", connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", commentId);
                insertCmd.Parameters.AddWithValue("@StoryId", Guid.Parse(id));
                insertCmd.Parameters.AddWithValue("@UserId", Guid.Parse(request.UserId));
                insertCmd.Parameters.AddWithValue("@Message", request.Message.Trim());
                insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync();
            }

            // Get user name for response
            string userName = "Usuario";
            using (var userCmd = new SqlCommand("SELECT Name FROM Users WHERE CAST(Id AS NVARCHAR(36)) = @UserId", connection))
            {
                userCmd.Parameters.AddWithValue("@UserId", request.UserId);
                var result = await userCmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    userName = result.ToString() ?? "Usuario";
                }
            }

            return Results.Ok(new StoryCommentDto
            {
                Id = commentId.ToString(),
                StoryId = id,
                UserId = request.UserId,
                Message = request.Message.Trim(),
                CreatedAt = createdAt,
                UserName = userName
            });
        });

        group.MapDelete("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if story exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM UserStories WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var count = await checkCmd.ExecuteScalarAsync();
                if (count == null || (int)count == 0)
                {
                    return Results.NotFound();
                }
            }

            // Delete related tasks, comments, and history first
            using (var deleteTasksCmd = new SqlCommand("DELETE FROM Tasks WHERE CAST(StoryId AS NVARCHAR(36)) = @StoryId", connection))
            {
                deleteTasksCmd.Parameters.AddWithValue("@StoryId", id);
                await deleteTasksCmd.ExecuteNonQueryAsync();
            }

            // Delete the story
            using (var deleteStoryCmd = new SqlCommand("DELETE FROM UserStories WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                deleteStoryCmd.Parameters.AddWithValue("@Id", id);
                await deleteStoryCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Historia eliminada" });
        });
    }

    private static UserStoryDto ToStoryDto(UserStory story, AppDataStore store)
    {
        var tasks = store.Data.Tasks
            .Where(task => task.StoryId == story.Id)
            .Select(task =>
            {
                var assignedUser = store.Data.Users.FirstOrDefault(user => user.Id == task.AssignedToId);
                return new TaskItemDto
                {
                    Id = task.Id,
                    StoryId = task.StoryId,
                    Title = task.Title,
                    Description = task.Description,
                    EstimatedHours = task.EstimatedHours,
                    ActualHours = task.ActualHours,
                    Status = task.Status,
                    Priority = task.Priority,
                    AssignedToId = task.AssignedToId,
                    AssignedToName = assignedUser?.Name,
                    StoryTitle = story.Title,
                    CreatedAt = task.CreatedAt,
                    CompletedAt = task.CompletedAt
                };
            })
            .ToList();

        return new UserStoryDto
        {
            Id = story.Id,
            ProjectId = story.ProjectId,
            SprintId = story.SprintId,
            Title = story.Title,
            Description = story.Description,
            AcceptanceCriteria = story.AcceptanceCriteria,
            Key = story.Key,
            Status = story.Status,
            Priority = story.Priority,
            StoryPoints = story.StoryPoints,
            Type = story.Type,
            AssigneeId = story.AssigneeId,
            CreatedById = story.CreatedById,
            CreatedAt = story.CreatedAt,
            UpdatedAt = story.UpdatedAt,
            TaskCount = tasks.Count,
            CompletedTaskCount = tasks.Count(task => task.Status == "Done"),
            Tasks = tasks,
            Comments = store.Data.StoryComments
                .Where(comment => comment.StoryId == story.Id)
                .OrderByDescending(comment => comment.CreatedAt)
                .Select(comment => new StoryCommentDto
                {
                    Id = comment.Id,
                    StoryId = comment.StoryId,
                    UserId = comment.UserId,
                    Message = comment.Message,
                    CreatedAt = comment.CreatedAt,
                    UserName = store.Data.Users.FirstOrDefault(user => user.Id == comment.UserId)?.Name ?? "Usuario"
                })
                .ToList(),
            History = store.Data.StoryHistory
                .Where(item => item.StoryId == story.Id)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new StoryHistoryDto
                {
                    Id = item.Id,
                    StoryId = item.StoryId,
                    UserId = item.UserId,
                    EventType = item.EventType,
                    Message = item.Message,
                    CreatedAt = item.CreatedAt,
                    UserName = store.Data.Users.FirstOrDefault(user => user.Id == item.UserId)?.Name ?? "Sistema"
                })
                .ToList()
        };
    }

    private static void AddStoryHistory(AppDataStore store, string storyId, string? userId, string eventType, string message)
    {
        store.Data.StoryHistory.Add(new StoryHistoryEntry
        {
            Id = Guid.NewGuid().ToString(),
            StoryId = storyId,
            UserId = string.IsNullOrWhiteSpace(userId) ? "system" : userId!,
            EventType = eventType,
            Message = message,
            CreatedAt = DateTime.UtcNow
        });
    }
}
