using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class RetrospectiveRoutes
{
    public static void MapRetrospectiveRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/retrospectives");

        // Get retrospectives by project
        group.MapGet("/project/{projectId}", async (string projectId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(r.Id AS NVARCHAR(36)), CAST(r.SprintId AS NVARCHAR(36)) as SprintId, 
                       CAST(r.FacilitatorId AS NVARCHAR(36)) as FacilitatorId, r.Date, r.MoodRating,
                       r.Template, r.Notes, r.IsCompleted, r.CreatedAt,
                       s.Name as SprintName, u.Name as FacilitatorName
                FROM SprintRetrospectives r
                LEFT JOIN Sprints s ON r.SprintId = s.Id
                LEFT JOIN Users u ON r.FacilitatorId = u.Id
                WHERE s.ProjectId = @ProjectId
                ORDER BY r.Date DESC";

            var retrospectives = new List<SprintRetrospectiveDto>();
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@ProjectId", Guid.Parse(projectId));
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    retrospectives.Add(new SprintRetrospectiveDto
                    {
                        Id = reader.GetString(0),
                        SprintId = reader.GetString(1),
                        FacilitatorId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Date = reader.GetDateTime(3),
                        MoodRating = reader.GetDecimal(4),
                        Template = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
                        IsCompleted = reader.GetBoolean(7),
                        CreatedAt = reader.GetDateTime(8),
                        SprintName = reader.IsDBNull(9) ? null : reader.GetString(9),
                        FacilitatorName = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }
            }

            // Load items and action items for each retrospective
            foreach (var retrospective in retrospectives)
            {
                await LoadRetrospectiveItems(connection, retrospective);
                await LoadActionItems(connection, retrospective);
                await LoadParticipantCount(connection, retrospective);
            }

            return Results.Ok(retrospectives);
        });

        // Get retrospective by ID
        group.MapGet("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                SELECT CAST(r.Id AS NVARCHAR(36)), CAST(r.SprintId AS NVARCHAR(36)) as SprintId, 
                       CAST(r.FacilitatorId AS NVARCHAR(36)) as FacilitatorId, r.Date, r.MoodRating,
                       r.Template, r.Notes, r.IsCompleted, r.CreatedAt,
                       s.Name as SprintName, u.Name as FacilitatorName
                FROM SprintRetrospectives r
                LEFT JOIN Sprints s ON r.SprintId = s.Id
                LEFT JOIN Users u ON r.FacilitatorId = u.Id
                WHERE r.Id = @Id";

            SprintRetrospectiveDto? retrospective = null;
            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@Id", Guid.Parse(id));
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    retrospective = new SprintRetrospectiveDto
                    {
                        Id = reader.GetString(0),
                        SprintId = reader.GetString(1),
                        FacilitatorId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Date = reader.GetDateTime(3),
                        MoodRating = reader.GetDecimal(4),
                        Template = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
                        IsCompleted = reader.GetBoolean(7),
                        CreatedAt = reader.GetDateTime(8),
                        SprintName = reader.IsDBNull(9) ? null : reader.GetString(9),
                        FacilitatorName = reader.IsDBNull(10) ? null : reader.GetString(10)
                    };
                }
            }

            if (retrospective == null) return Results.NotFound();

            await LoadRetrospectiveItems(connection, retrospective);
            await LoadActionItems(connection, retrospective);
            await LoadParticipantCount(connection, retrospective);

            return Results.Ok(retrospective);
        });

        // Create retrospective
        group.MapPost("/", async (CreateRetrospectiveRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var retrospectiveId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;

            var sql = @"
                INSERT INTO SprintRetrospectives (Id, SprintId, FacilitatorId, Date, MoodRating, Template, Notes, IsCompleted, CreatedAt)
                VALUES (@Id, @SprintId, @FacilitatorId, @Date, @MoodRating, @Template, @Notes, @IsCompleted, @CreatedAt)";

            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@Id", retrospectiveId);
                cmd.Parameters.AddWithValue("@SprintId", Guid.Parse(request.SprintId));
                cmd.Parameters.AddWithValue("@FacilitatorId", Guid.Parse(request.FacilitatorId));
                cmd.Parameters.AddWithValue("@Date", createdAt);
                cmd.Parameters.AddWithValue("@MoodRating", request.MoodRating);
                cmd.Parameters.AddWithValue("@Template", request.Template ?? "StartStopContinue");
                cmd.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsCompleted", false);
                cmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await cmd.ExecuteNonQueryAsync();
            }

            return Results.Created($"/api/retrospectives/{retrospectiveId}", new { Id = retrospectiveId.ToString() });
        });

        // Update retrospective
        group.MapPut("/{id}", async (string id, UpdateRetrospectiveRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                UPDATE SprintRetrospectives 
                SET MoodRating = @MoodRating, Notes = @Notes, IsCompleted = @IsCompleted, UpdatedAt = @UpdatedAt
                WHERE Id = @Id";

            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@Id", Guid.Parse(id));
                cmd.Parameters.AddWithValue("@MoodRating", request.MoodRating);
                cmd.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsCompleted", request.IsCompleted);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected == 0) return Results.NotFound();
            }

            return Results.Ok();
        });

        // Add retrospective item
        group.MapPost("/{id}/items", async (string id, CreateRetrospectiveItemRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var itemId = Guid.NewGuid();

            var sql = @"
                INSERT INTO RetrospectiveItems (Id, RetrospectiveId, Type, Content, UserId, Votes, CreatedAt)
                VALUES (@Id, @RetrospectiveId, @Type, @Content, @UserId, @Votes, @CreatedAt)";

            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@Id", itemId);
                cmd.Parameters.AddWithValue("@RetrospectiveId", Guid.Parse(id));
                cmd.Parameters.AddWithValue("@Type", request.Type);
                cmd.Parameters.AddWithValue("@Content", request.Content);
                cmd.Parameters.AddWithValue("@UserId", string.IsNullOrWhiteSpace(request.UserId) ? DBNull.Value : Guid.Parse(request.UserId));
                cmd.Parameters.AddWithValue("@Votes", 0);
                cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync();
            }

            return Results.Created($"/api/retrospectives/{id}/items/{itemId}", new { Id = itemId.ToString() });
        });

        // Add action item
        group.MapPost("/{id}/action-items", async (string id, CreateRetrospectiveActionItemRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var actionItemId = Guid.NewGuid();

            var sql = @"
                INSERT INTO RetrospectiveActionItems (Id, RetrospectiveId, Action, AssignedToId, DueDate, Status, CreatedById, CreatedAt)
                VALUES (@Id, @RetrospectiveId, @Action, @AssignedToId, @DueDate, @Status, @CreatedById, @CreatedAt)";

            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@Id", actionItemId);
                cmd.Parameters.AddWithValue("@RetrospectiveId", Guid.Parse(id));
                cmd.Parameters.AddWithValue("@Action", request.Action);
                cmd.Parameters.AddWithValue("@AssignedToId", Guid.Parse(request.AssignedToId));
                cmd.Parameters.AddWithValue("@DueDate", request.DueDate);
                cmd.Parameters.AddWithValue("@Status", "Pending");
                cmd.Parameters.AddWithValue("@CreatedById", Guid.Parse(request.CreatedById));
                cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync();
            }

            return Results.Created($"/api/retrospectives/{id}/action-items/{actionItemId}", new { Id = actionItemId.ToString() });
        });

        // Update action item status
        group.MapPatch("/action-items/{itemId}", async (string itemId, UpdateActionItemRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = @"
                UPDATE RetrospectiveActionItems 
                SET Status = @Status, CompletedAt = CASE WHEN @Status = 'Completed' THEN GETDATE() ELSE CompletedAt END
                WHERE Id = @ItemId";

            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@ItemId", Guid.Parse(itemId));
                cmd.Parameters.AddWithValue("@Status", request.Status);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected == 0) return Results.NotFound();
            }

            return Results.Ok();
        });

        // Delete retrospective
        group.MapDelete("/{retrospectiveId}", async (string retrospectiveId, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if retrospective exists
            var checkSql = "SELECT COUNT(*) FROM SprintRetrospectives WHERE Id = @RetrospectiveId";
            using (var checkCmd = new SqlCommand(checkSql, connection))
            {
                checkCmd.Parameters.AddWithValue("@RetrospectiveId", Guid.Parse(retrospectiveId));
                var exists = (int)await checkCmd.ExecuteScalarAsync();
                
                if (exists == 0) return Results.NotFound();
            }

            // Delete related items in order (due to foreign key constraints)
            var deleteItemsSql = "DELETE FROM RetrospectiveItems WHERE RetrospectiveId = @RetrospectiveId";
            using (var deleteItemsCmd = new SqlCommand(deleteItemsSql, connection))
            {
                deleteItemsCmd.Parameters.AddWithValue("@RetrospectiveId", Guid.Parse(retrospectiveId));
                await deleteItemsCmd.ExecuteNonQueryAsync();
            }

            var deleteActionItemsSql = "DELETE FROM RetrospectiveActionItems WHERE RetrospectiveId = @RetrospectiveId";
            using (var deleteActionItemsCmd = new SqlCommand(deleteActionItemsSql, connection))
            {
                deleteActionItemsCmd.Parameters.AddWithValue("@RetrospectiveId", Guid.Parse(retrospectiveId));
                await deleteActionItemsCmd.ExecuteNonQueryAsync();
            }

            // Delete the retrospective
            var deleteRetrospectiveSql = "DELETE FROM SprintRetrospectives WHERE Id = @RetrospectiveId";
            using (var deleteRetrospectiveCmd = new SqlCommand(deleteRetrospectiveSql, connection))
            {
                deleteRetrospectiveCmd.Parameters.AddWithValue("@RetrospectiveId", Guid.Parse(retrospectiveId));
                var rowsAffected = await deleteRetrospectiveCmd.ExecuteNonQueryAsync();

                if (rowsAffected == 0) return Results.NotFound();
            }

            return Results.Ok(new { message = "Retrospective eliminada exitosamente" });
        });
    }

    private static async Task LoadRetrospectiveItems(SqlConnection connection, SprintRetrospectiveDto retrospective)
    {
        var sql = @"
            SELECT CAST(ri.Id AS NVARCHAR(36)), CAST(ri.RetrospectiveId AS NVARCHAR(36)) as RetrospectiveId,
                   ri.Type, ri.Content, ri.Votes, ri.CreatedAt,
                   CAST(ri.UserId AS NVARCHAR(36)) as UserId, u.Name as UserName
            FROM RetrospectiveItems ri
            LEFT JOIN Users u ON ri.UserId = u.Id
            WHERE ri.RetrospectiveId = @RetrospectiveId
            ORDER BY ri.Votes DESC, ri.CreatedAt ASC";

        using (var cmd = new SqlCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@RetrospectiveId", Guid.Parse(retrospective.Id));
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                retrospective.Items.Add(new RetrospectiveItemDto
                {
                    Id = reader.GetString(0),
                    RetrospectiveId = reader.GetString(1),
                    Type = reader.GetString(2),
                    Content = reader.GetString(3),
                    Votes = reader.GetInt32(4),
                    CreatedAt = reader.GetDateTime(5),
                    UserId = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    UserName = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
        }
    }

    private static async Task LoadActionItems(SqlConnection connection, SprintRetrospectiveDto retrospective)
    {
        var sql = @"
            SELECT CAST(rai.Id AS NVARCHAR(36)), CAST(rai.RetrospectiveId AS NVARCHAR(36)) as RetrospectiveId,
                   rai.Action, CAST(rai.AssignedToId AS NVARCHAR(36)) as AssignedToId, rai.DueDate, rai.Status,
                   rai.CompletedAt, CAST(rai.CreatedById AS NVARCHAR(36)) as CreatedById, rai.CreatedAt,
                   assigned.Name as AssignedToName, creator.Name as CreatedByName
            FROM RetrospectiveActionItems rai
            LEFT JOIN Users assigned ON rai.AssignedToId = assigned.Id
            LEFT JOIN Users creator ON rai.CreatedById = creator.Id
            WHERE rai.RetrospectiveId = @RetrospectiveId
            ORDER BY rai.DueDate ASC";

        using (var cmd = new SqlCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@RetrospectiveId", Guid.Parse(retrospective.Id));
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                retrospective.ActionItems.Add(new RetrospectiveActionItemDto
                {
                    Id = reader.GetString(0),
                    RetrospectiveId = reader.GetString(1),
                    Action = reader.GetString(2),
                    AssignedToId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    DueDate = reader.GetDateTime(4),
                    Status = reader.GetString(5),
                    CompletedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    CreatedById = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    CreatedAt = reader.GetDateTime(8),
                    AssignedToName = reader.IsDBNull(9) ? null : reader.GetString(9),
                    CreatedByName = reader.IsDBNull(10) ? null : reader.GetString(10)
                });
            }
        }
    }

    private static async Task LoadParticipantCount(SqlConnection connection, SprintRetrospectiveDto retrospective)
    {
        // Validate retrospective ID
        if (string.IsNullOrEmpty(retrospective.Id) || !Guid.TryParse(retrospective.Id, out Guid retrospectiveGuid))
        {
            retrospective.ParticipantCount = 1; // Default to facilitator if ID is invalid
            return;
        }

        // Count unique participants who have added items (excluding anonymous)
        var sql = @"
            SELECT COUNT(DISTINCT UserId) 
            FROM RetrospectiveItems 
            WHERE RetrospectiveId = @RetrospectiveId 
            AND UserId IS NOT NULL";

        using (var cmd = new SqlCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@RetrospectiveId", retrospectiveGuid);
            
            try
            {
                var result = await cmd.ExecuteScalarAsync();
                var participantCount = result != null ? Convert.ToInt32(result) : 0;
                
                // If no participants have added items yet, show the facilitator as participant
                if (participantCount == 0) {
                    retrospective.ParticipantCount = 1; // At least the facilitator
                } else {
                    retrospective.ParticipantCount = participantCount;
                }
            }
            catch (Exception)
            {
                // If there's any error in counting, default to facilitator count
                retrospective.ParticipantCount = 1;
            }
        }
    }
}
