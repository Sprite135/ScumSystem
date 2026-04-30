using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Models;
using TaskStatus = ScrumSystem.Api.Models.TaskStatus;

namespace ScrumSystem.Api.Routes;

public static class TaskRoutes
{
    public static void MapTaskRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks");

        // Create task
        group.MapPost("/", async (CreateTaskRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var id = Guid.NewGuid();

                var sql = @"
                    INSERT INTO Tasks (Id, StoryId, Title, Description, EstimatedHours)
                    VALUES (@Id, @StoryId, @Title, @Description, @EstimatedHours)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@StoryId", request.StoryId);
                cmd.Parameters.AddWithValue("@Title", request.Title);
                cmd.Parameters.AddWithValue("@Description", (object?)request.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EstimatedHours", (object?)request.EstimatedHours ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync();

                return Results.Created($"/api/tasks/{id}", new TaskItem
                {
                    Id = id,
                    StoryId = request.StoryId,
                    Title = request.Title,
                    Description = request.Description,
                    EstimatedHours = request.EstimatedHours,
                    Status = Models.TaskStatus.Todo,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating task: {ex.Message}");
            }
        });

        // Get tasks by story
        group.MapGet("/story/{storyId:guid}", async (Guid storyId, DatabaseContext db) =>
        {
            var tasks = new List<TaskItemDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT t.*, u.Name as AssignedToName
                FROM Tasks t
                LEFT JOIN Users u ON t.AssignedTo = u.Id
                WHERE t.StoryId = @StoryId
                ORDER BY t.CreatedAt";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@StoryId", storyId);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tasks.Add(MapTaskItemDto(reader));
            }

            return Results.Ok(tasks);
        });

        // Update task status
        group.MapPatch("/{id:guid}/status", async (Guid id, UpdateTaskStatusRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var completedAt = request.Status == Models.TaskStatus.Done.ToString() ? (object)DateTime.UtcNow : DBNull.Value;

                var sql = @"
                    UPDATE Tasks 
                    SET Status = @Status, ActualHours = @ActualHours, CompletedAt = @CompletedAt
                    WHERE Id = @Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Status", request.Status);
                cmd.Parameters.AddWithValue("@ActualHours", request.ActualHours);
                cmd.Parameters.AddWithValue("@CompletedAt", completedAt);
                cmd.Parameters.AddWithValue("@Id", id);

                await cmd.ExecuteNonQueryAsync();
                return Results.Ok(new { message = "Status updated" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error updating status: {ex.Message}");
            }
        });

        // Assign task
        group.MapPatch("/{id:guid}/assign", async (Guid id, Guid assignedTo, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = "UPDATE Tasks SET AssignedTo = @AssignedTo WHERE Id = @Id";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@AssignedTo", assignedTo);
                cmd.Parameters.AddWithValue("@Id", id);

                await cmd.ExecuteNonQueryAsync();
                return Results.Ok(new { message = "Task assigned" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error assigning task: {ex.Message}");
            }
        });

        // Get Kanban board for sprint
        group.MapGet("/board/{sprintId:guid}", async (Guid sprintId, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT t.*, u.Name as AssignedToName, us.Title as StoryTitle
                FROM Tasks t
                JOIN UserStories us ON t.StoryId = us.Id
                LEFT JOIN Users u ON t.AssignedTo = u.Id
                WHERE us.SprintId = @SprintId
                ORDER BY 
                    CASE t.Status
                        WHEN 'Todo' THEN 1
                        WHEN 'InProgress' THEN 2
                        WHEN 'Blocked' THEN 3
                        WHEN 'Done' THEN 4
                    END";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SprintId", sprintId);
            using var reader = await cmd.ExecuteReaderAsync();

            var board = new KanbanBoardDto();

            while (await reader.ReadAsync())
            {
                var task = MapTaskItemDto(reader);
                switch (task.Status)
                {
                    case Models.TaskStatus.Todo:
                        board.Todo.Add(task);
                        break;
                    case Models.TaskStatus.InProgress:
                        board.InProgress.Add(task);
                        break;
                    case Models.TaskStatus.Done:
                        board.Done.Add(task);
                        break;
                    case Models.TaskStatus.Blocked:
                        board.Blocked.Add(task);
                        break;
                }
            }

            return Results.Ok(board);
        });

        // Get task by ID
        group.MapGet("/{id:guid}", async (Guid id, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT t.*, u.Name as AssignedToName
                FROM Tasks t
                LEFT JOIN Users u ON t.AssignedTo = u.Id
                WHERE t.Id = @Id";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.NotFound();
            }

            return Results.Ok(MapTaskItemDto(reader));
        });

        // Update task
        group.MapPut("/{id:guid}", async (Guid id, CreateTaskRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = @"
                    UPDATE Tasks 
                    SET StoryId = @StoryId, 
                        Title = @Title, 
                        Description = @Description, 
                        EstimatedHours = @EstimatedHours
                    WHERE Id = @Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@StoryId", request.StoryId);
                cmd.Parameters.AddWithValue("@Title", request.Title);
                cmd.Parameters.AddWithValue("@Description", (object?)request.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EstimatedHours", (object?)request.EstimatedHours ?? DBNull.Value);

                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected == 0) return Results.NotFound();

                return Results.Ok(new { message = "Tarea actualizada exitosamente" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error updating task: {ex.Message}");
            }
        });

        // Delete task
        group.MapDelete("/{id:guid}", async (Guid id, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = "DELETE FROM Tasks WHERE Id = @Id";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);

                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected == 0) return Results.NotFound();

                return Results.Ok(new { message = "Tarea eliminada exitosamente" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error deleting task: {ex.Message}");
            }
        });
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
            Status = Enum.Parse<Models.TaskStatus>(reader["Status"].ToString()!),
            AssignedTo = reader["AssignedTo"] as Guid?,
            AssignedToName = reader["AssignedToName"]?.ToString(),
            StoryTitle = reader["StoryTitle"]?.ToString(),
            CreatedAt = (DateTime)reader["CreatedAt"],
            CompletedAt = reader["CompletedAt"] as DateTime?
        };
    }
}

public class UpdateTaskStatusRequest
{
    public string Status { get; set; } = "";
    public int ActualHours { get; set; }
}

public class KanbanBoardDto
{
    public List<TaskItemDto> Todo { get; set; } = new();
    public List<TaskItemDto> InProgress { get; set; } = new();
    public List<TaskItemDto> Done { get; set; } = new();
    public List<TaskItemDto> Blocked { get; set; } = new();
}
