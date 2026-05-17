using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class TaskRoutes
{
    public static void MapTaskRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks");

        group.MapPost("/", async (CreateTaskRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verify story exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM UserStories WHERE CAST(Id AS NVARCHAR(36)) = @StoryId", connection))
            {
                checkCmd.Parameters.AddWithValue("@StoryId", request.StoryId);
                var count = (int)await checkCmd.ExecuteScalarAsync();
                if (count == 0)
                {
                    return Results.BadRequest("Historia no encontrada");
                }
            }

            var taskId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;
            var status = request.Status ?? "Todo";

            // Insert task
            var insertSql = @"
                INSERT INTO Tasks (Id, StoryId, Title, Description, EstimatedHours, ActualHours, Status, Priority, AssignedToId, CreatedAt) 
                VALUES (@Id, @StoryId, @Title, @Description, @EstimatedHours, @ActualHours, @Status, @Priority, @AssignedToId, @CreatedAt)";

            using (var insertCmd = new SqlCommand(insertSql, connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", taskId);
                insertCmd.Parameters.AddWithValue("@StoryId", Guid.Parse(request.StoryId));
                insertCmd.Parameters.AddWithValue("@Title", request.Title.Trim());
                insertCmd.Parameters.AddWithValue("@Description", (object?)request.Description?.Trim() ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@EstimatedHours", (object?)request.EstimatedHours ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@ActualHours", 0);
                insertCmd.Parameters.AddWithValue("@Status", status);
                insertCmd.Parameters.AddWithValue("@Priority", request.Priority);
                insertCmd.Parameters.AddWithValue("@AssignedToId", 
                    !string.IsNullOrEmpty(request.AssignedToId) ? (object)Guid.Parse(request.AssignedToId) : DBNull.Value);
                insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync();
            }

            // Add story history
            await AddStoryHistoryAsync(connection, request.StoryId, request.AssignedToId, "SubtaskCreated", $"Subtarea creada: {request.Title.Trim()}");

            var taskDto = await GetTaskByIdAsync(connection, taskId.ToString());
            return Results.Created($"/api/tasks/{taskId}", taskDto);
        });

        group.MapGet("/story/{storyId}", async (string storyId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT t.Id, CAST(t.StoryId AS NVARCHAR(36)), t.Title, t.Description, t.EstimatedHours, t.ActualHours, 
                       t.Status, CAST(t.AssignedToId AS NVARCHAR(36)), u.Name as AssignedToName, t.CreatedAt
                FROM Tasks t
                LEFT JOIN Users u ON t.AssignedToId = u.Id
                WHERE CAST(t.StoryId AS NVARCHAR(36)) = @StoryId
                ORDER BY t.CreatedAt";

            var tasks = new List<TaskItemDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@StoryId", storyId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tasks.Add(new TaskItemDto
                    {
                        Id = reader.GetGuid(0).ToString(),
                        StoryId = reader.GetString(1),
                        Title = reader.GetString(2),
                        Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                        EstimatedHours = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        ActualHours = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        Status = reader.GetString(6),
                        AssignedToId = reader.IsDBNull(7) ? null : reader.GetString(7),
                        AssignedToName = reader.IsDBNull(8) ? null : reader.GetString(8),
                        CreatedAt = reader.GetDateTime(9)
                    });
                }
            }

            return Results.Ok(tasks);
        });

        group.MapPatch("/{id}/status", async (string id, UpdateTaskStatusRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Get current task
            var currentTask = await GetTaskByIdAsync(connection, id);
            if (currentTask is null)
            {
                return Results.NotFound();
            }

            var completedAt = request.Status == "Done" ? DateTime.UtcNow : (DateTime?)null;

            // Update task status
            var updateSql = @"
                UPDATE Tasks 
                SET Status = @Status, ActualHours = @ActualHours
                WHERE CAST(Id AS NVARCHAR(36)) = @Id";

            using (var updateCmd = new SqlCommand(updateSql, connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@Status", request.Status);
                updateCmd.Parameters.AddWithValue("@ActualHours", request.ActualHours);
                await updateCmd.ExecuteNonQueryAsync();
            }

            await AddStoryHistoryAsync(connection, currentTask.StoryId, currentTask.AssignedToId, "SubtaskStatus", $"Subtarea '{currentTask.Title}' cambio a {request.Status}.");
            return Results.Ok(new { message = "Status updated" });
        });

        group.MapPatch("/{id}/assign", async (string id, UpdateTaskAssigneeRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Get current task
            var currentTask = await GetTaskByIdAsync(connection, id);
            if (currentTask is null)
            {
                return Results.NotFound();
            }

            Console.WriteLine($"[TaskRoutes] Assigning task {id} to user {request.AssignedToId}");
            Console.WriteLine($"[TaskRoutes] Current assignedToId: {currentTask.AssignedToId}");

            // Update task assignment
            var updateSql = @"
                UPDATE Tasks 
                SET AssignedToId = @AssignedToId
                WHERE CAST(Id AS NVARCHAR(36)) = @Id";

            using (var updateCmd = new SqlCommand(updateSql, connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@AssignedToId", 
                    !string.IsNullOrEmpty(request.AssignedToId) ? (object)Guid.Parse(request.AssignedToId) : DBNull.Value);
                var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                Console.WriteLine($"[TaskRoutes] Update affected {rowsAffected} rows");
            }

            await AddStoryHistoryAsync(connection, currentTask.StoryId, request.AssignedToId, "SubtaskAssigned", $"Subtarea '{currentTask.Title}' asignada.");
            
            var updatedTask = await GetTaskByIdAsync(connection, id);
            Console.WriteLine($"[TaskRoutes] Updated task assignedToId: {updatedTask.AssignedToId}");
            return Results.Ok(updatedTask);
        });

        group.MapPatch("/{id}/description", async (string id, UpdateTaskDescriptionRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Get current task
            var currentTask = await GetTaskByIdAsync(connection, id);
            if (currentTask is null)
            {
                return Results.NotFound();
            }

            // Update task description
            var updateSql = @"
                UPDATE Tasks 
                SET Description = @Description, UpdatedAt = @UpdatedAt
                WHERE CAST(Id AS NVARCHAR(36)) = @Id";

            using (var updateCmd = new SqlCommand(updateSql, connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@Description", 
                    !string.IsNullOrEmpty(request.Description) ? request.Description : DBNull.Value);
                updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);
                await updateCmd.ExecuteNonQueryAsync();
            }

            await AddStoryHistoryAsync(connection, currentTask.StoryId, null, "SubtaskUpdated", $"Descripción de la subtarea '{currentTask.Title}' actualizada.");
            
            var updatedTask = await GetTaskByIdAsync(connection, id);
            return Results.Ok(new { message = "Descripción actualizada exitosamente" });
        });

        group.MapGet("/board/{sprintId}", async (string sprintId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(t.Id AS NVARCHAR(36)) as Id, CAST(t.StoryId AS NVARCHAR(36)) as StoryId, t.Title, t.Description, 
                       t.EstimatedHours, t.ActualHours, t.Status, t.Priority, 
                       CAST(t.AssignedToId AS NVARCHAR(36)) as AssignedToId, u.Name as AssignedToName,
                       us.Title as StoryTitle, t.CreatedAt
                FROM Tasks t
                LEFT JOIN Users u ON t.AssignedToId = u.Id
                LEFT JOIN UserStories us ON t.StoryId = us.Id
                WHERE CAST(us.SprintId AS NVARCHAR(36)) = @SprintId
                ORDER BY t.CreatedAt DESC";

            var tasks = new List<TaskItemDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@SprintId", sprintId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tasks.Add(new TaskItemDto
                    {
                        Id = reader.GetString(0),
                        StoryId = reader.GetString(1),
                        Title = reader.GetString(2),
                        Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                        EstimatedHours = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        ActualHours = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        Status = reader.GetString(6),
                        Priority = reader.GetInt32(7),
                        AssignedToId = reader.IsDBNull(8) ? null : reader.GetString(8),
                        AssignedToName = reader.IsDBNull(9) ? null : reader.GetString(9),
                        StoryTitle = reader.GetString(10),
                        CreatedAt = reader.GetDateTime(11)
                    });
                }
            }

            var board = new KanbanBoardDto
            {
                Todo = tasks.Where(task => task.Status == "Todo").ToList(),
                InProgress = tasks.Where(task => task.Status == "InProgress").ToList(),
                Done = tasks.Where(task => task.Status == "Done").ToList(),
                Blocked = tasks.Where(task => task.Status == "Blocked").ToList()
            };

            return Results.Ok(board);
        });

        group.MapGet("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var taskDto = await GetTaskByIdAsync(connection, id);
            return taskDto is null ? Results.NotFound() : Results.Ok(taskDto);
        });

        group.MapPut("/{id}", async (string id, CreateTaskRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if task exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Tasks WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var count = await checkCmd.ExecuteScalarAsync();
                if (count == null || (int)count == 0)
                {
                    return Results.NotFound();
                }
            }

            // Update task
            using (var updateCmd = new SqlCommand(@"
                UPDATE Tasks 
                SET StoryId = @StoryId, Title = @Title, Description = @Description, 
                    EstimatedHours = @EstimatedHours, Priority = @Priority, UpdatedAt = @UpdatedAt
                WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", Guid.Parse(id));
                updateCmd.Parameters.AddWithValue("@StoryId", Guid.Parse(request.StoryId));
                updateCmd.Parameters.AddWithValue("@Title", request.Title.Trim());
                updateCmd.Parameters.AddWithValue("@Description", (object?)request.Description?.Trim() ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@EstimatedHours", (object?)request.EstimatedHours ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Priority", request.Priority);
                updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);
                await updateCmd.ExecuteNonQueryAsync();
            }

            await AddStoryHistoryAsync(connection, request.StoryId, null, "SubtaskUpdated", $"Subtarea actualizada: {request.Title}");
            
            return Results.Ok(new { message = "Tarea actualizada exitosamente" });
        });

        group.MapDelete("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Get task info for history before deleting
            string taskTitle = "";
            string storyId = "";
            using (var getTaskCmd = new SqlCommand("SELECT Title, CAST(StoryId AS NVARCHAR(36)) FROM Tasks WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                getTaskCmd.Parameters.AddWithValue("@Id", id);
                using var reader = await getTaskCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    taskTitle = reader.GetString(0);
                    storyId = reader.GetString(1);
                }
                else
                {
                    return Results.NotFound();
                }
            }

            // Delete the task
            using (var deleteCmd = new SqlCommand("DELETE FROM Tasks WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                deleteCmd.Parameters.AddWithValue("@Id", id);
                await deleteCmd.ExecuteNonQueryAsync();
            }

            await AddStoryHistoryAsync(connection, storyId, null, "SubtaskDeleted", $"Subtarea eliminada: {taskTitle}");
            
            return Results.Ok(new { message = "Tarea eliminada exitosamente" });
        });
    }

    private static TaskItemDto ToTaskDto(TaskItem task, AppDataStore store)
    {
        var assignedUser = store.Data.Users.FirstOrDefault(user => user.Id == task.AssignedToId);
        var story = store.Data.UserStories.FirstOrDefault(item => item.Id == task.StoryId);

        return new TaskItemDto
        {
            Id = task.Id,
            StoryId = task.StoryId,
            Title = task.Title,
            Description = task.Description,
            EstimatedHours = task.EstimatedHours,
            ActualHours = task.ActualHours,
            Status = task.Status,
            AssignedToId = task.AssignedToId,
            AssignedToName = assignedUser?.Name,
            StoryTitle = story?.Title,
            CreatedAt = task.CreatedAt,
            CompletedAt = task.CompletedAt
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

    private static async Task<TaskItemDto?> GetTaskByIdAsync(SqlConnection connection, string taskId)
    {
        var sql = @"
            SELECT t.Id, t.StoryId, t.Title, t.Description, t.EstimatedHours, t.ActualHours, 
                   t.Status, t.AssignedToId, u.Name as AssignedToName, t.CreatedAt
            FROM Tasks t
            LEFT JOIN Users u ON t.AssignedToId = u.Id
            WHERE t.Id = @TaskId";

        using (var cmd = new SqlCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@TaskId", Guid.Parse(taskId));
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TaskItemDto
                {
                    Id = reader.GetGuid(0).ToString(),
                    StoryId = reader.GetGuid(1).ToString(),
                    Title = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                    EstimatedHours = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    ActualHours = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    Status = reader.GetString(6),
                    AssignedToId = reader.IsDBNull(7) ? null : reader.GetGuid(7).ToString(),
                    AssignedToName = reader.IsDBNull(8) ? null : reader.GetString(8),
                    CreatedAt = reader.GetDateTime(9)
                };
            }
        }

        return null;
    }

    private static async Task AddStoryHistoryAsync(SqlConnection connection, string storyId, string? userId, string eventType, string message)
    {
        var historyId = Guid.NewGuid();
        var sql = @"
            INSERT INTO StoryHistory (Id, StoryId, UserId, EventType, Message, CreatedAt)
            VALUES (@Id, @StoryId, @UserId, @EventType, @Message, @CreatedAt)";

        using (var cmd = new SqlCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@Id", historyId);
            cmd.Parameters.AddWithValue("@StoryId", Guid.Parse(storyId));
            cmd.Parameters.AddWithValue("@UserId", 
                !string.IsNullOrEmpty(userId) ? (object)Guid.Parse(userId) : DBNull.Value);
            cmd.Parameters.AddWithValue("@EventType", eventType);
            cmd.Parameters.AddWithValue("@Message", message);
            cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

public class KanbanBoardDto
{
    public List<TaskItemDto> Todo { get; set; } = new();
    public List<TaskItemDto> InProgress { get; set; } = new();
    public List<TaskItemDto> Done { get; set; } = new();
    public List<TaskItemDto> Blocked { get; set; } = new();
}
