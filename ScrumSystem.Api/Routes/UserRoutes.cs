using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class UserRoutes
{
    public static void MapUserRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapPost("/register", async (RegisterRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if email exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Email = @Email", connection))
            {
                checkCmd.Parameters.AddWithValue("@Email", request.Email.Trim());
                var count = (int)await checkCmd.ExecuteScalarAsync();
                if (count > 0)
                {
                    return Results.BadRequest("El email ya está registrado");
                }
            }

            var userId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;
            var avatarColor = BuildAvatarColor();

            // Insert user
            var insertSql = @"
                INSERT INTO Users (Id, Name, Email, PasswordHash, Role, AvatarColor, CreatedAt) 
                VALUES (@Id, @Name, @Email, @PasswordHash, @Role, @AvatarColor, @CreatedAt)";
            
            using (var insertCmd = new SqlCommand(insertSql, connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", userId);
                insertCmd.Parameters.AddWithValue("@Name", request.Name.Trim());
                insertCmd.Parameters.AddWithValue("@Email", request.Email.Trim());
                insertCmd.Parameters.AddWithValue("@PasswordHash", BCrypt.Net.BCrypt.HashPassword(request.Password));
                insertCmd.Parameters.AddWithValue("@Role", (int)ParseRole(request.Role));
                insertCmd.Parameters.AddWithValue("@AvatarColor", avatarColor);
                insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Usuario registrado exitosamente", userId = userId.ToString() });
        });

        group.MapPost("/", async (CreateUserRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Check if email exists
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Email = @Email", connection))
            {
                checkCmd.Parameters.AddWithValue("@Email", request.Email.Trim());
                var count = (int)await checkCmd.ExecuteScalarAsync();
                if (count > 0)
                {
                    return Results.BadRequest("Email already exists");
                }
            }

            var userId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;
            var avatarColor = BuildAvatarColor();

            // Insert user
            var insertSql = @"
                INSERT INTO Users (Id, Name, Email, PasswordHash, Role, AvatarColor, CreatedAt) 
                VALUES (@Id, @Name, @Email, @PasswordHash, @Role, @AvatarColor, @CreatedAt)";
            
            using (var insertCmd = new SqlCommand(insertSql, connection))
            {
                insertCmd.Parameters.AddWithValue("@Id", userId);
                insertCmd.Parameters.AddWithValue("@Name", request.Name.Trim());
                insertCmd.Parameters.AddWithValue("@Email", request.Email.Trim());
                insertCmd.Parameters.AddWithValue("@PasswordHash", BCrypt.Net.BCrypt.HashPassword(request.Password));
                insertCmd.Parameters.AddWithValue("@Role", (int)request.Role);
                insertCmd.Parameters.AddWithValue("@AvatarColor", avatarColor);
                insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync();
            }

            var userDto = new UserDto
            {
                Id = userId.ToString(),
                Name = request.Name.Trim(),
                Email = request.Email.Trim(),
                Role = request.Role,
                CreatedAt = createdAt
            };

            return Results.Created($"/api/users/{userId}", userDto);
        });

        group.MapPost("/login", async (LoginRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            User? user = null;
            using (var cmd = new SqlCommand("SELECT CAST(Id AS NVARCHAR(36)), Name, Email, PasswordHash, Role, CreatedAt FROM Users WHERE Email = @Email", connection))
            {
                cmd.Parameters.AddWithValue("@Email", request.Email.Trim());
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    user = new User
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Email = reader.GetString(2),
                        PasswordHash = reader.GetString(3),
                        Role = Enum.Parse<UserRole>(reader.GetString(4)),
                        CreatedAt = reader.GetDateTime(5)
                    };
                }
            }

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(ToUserDto(user));
        });

        group.MapGet("/", async (DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            var sql = "SELECT CAST(Id AS NVARCHAR(36)), Name, Email, Role, CreatedAt FROM Users ORDER BY Name";
            var users = new List<UserDto>();
            
            using (var cmd = new SqlCommand(sql, connection))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
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

        group.MapGet("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using (var cmd = new SqlCommand("SELECT CAST(Id AS NVARCHAR(36)), Name, Email, Role, CreatedAt FROM Users WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var userDto = new UserDto
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Email = reader.GetString(2),
                        Role = Enum.Parse<UserRole>(reader.GetString(3)),
                        CreatedAt = reader.GetDateTime(4)
                    };
                    return Results.Ok(userDto);
                }
            }

            return Results.NotFound();
        });

        group.MapGet("/search", async (string email, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using (var cmd = new SqlCommand("SELECT CAST(Id AS NVARCHAR(36)), Name, Email, Role, CreatedAt FROM Users WHERE Email = @Email", connection))
            {
                cmd.Parameters.AddWithValue("@Email", email);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var userDto = new UserDto
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Email = reader.GetString(2),
                        Role = Enum.Parse<UserRole>(reader.GetString(3)),
                        CreatedAt = reader.GetDateTime(4)
                    };
                    return Results.Ok(userDto);
                }
            }

            // Return empty object instead of 404 to avoid console error
            return Results.Ok(new { });
        });

        group.MapPut("/{id}", async (string id, UpdateUserRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Verificar que el usuario existe
            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var count = (int)await checkCmd.ExecuteScalarAsync();
                if (count == 0)
                {
                    return Results.NotFound(new { message = "Usuario no encontrado" });
                }
            }

            // Verificar que el email no está en uso por otro usuario
            using (var emailCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Email = @Email AND CAST(Id AS NVARCHAR(36)) != @Id", connection))
            {
                emailCmd.Parameters.AddWithValue("@Email", request.Email.Trim());
                emailCmd.Parameters.AddWithValue("@Id", id);
                var count = (int)await emailCmd.ExecuteScalarAsync();
                if (count > 0)
                {
                    return Results.BadRequest(new { message = "El email ya está en uso" });
                }
            }

            // Actualizar usuario
            using (var updateCmd = new SqlCommand(@"
                UPDATE Users 
                SET Name = @Name, Email = @Email
                WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@Name", request.Name.Trim());
                updateCmd.Parameters.AddWithValue("@Email", request.Email.Trim());
                await updateCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Usuario actualizado exitosamente" });
        });

        group.MapPut("/{id}/password", async (string id, ChangePasswordRequest request, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            // Obtener hash actual
            string currentHash = "";
            using (var checkCmd = new SqlCommand("SELECT PasswordHash FROM Users WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                checkCmd.Parameters.AddWithValue("@Id", id);
                var result = await checkCmd.ExecuteScalarAsync();
                if (result == null)
                {
                    return Results.NotFound(new { message = "Usuario no encontrado" });
                }
                currentHash = result.ToString()!;
            }

            // Verificar contraseña actual
            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, currentHash))
            {
                return Results.BadRequest(new { message = "Contraseña actual incorrecta" });
            }

            // Actualizar contraseña
            var newHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            using (var updateCmd = new SqlCommand("UPDATE Users SET PasswordHash = @PasswordHash WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection))
            {
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@PasswordHash", newHash);
                await updateCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { message = "Contraseña cambiada exitosamente" });
        });

        group.MapDelete("/{id}", async (string id, DatabaseContext dbContext) =>
        {
            using var connection = dbContext.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Verificar que el usuario existe
                using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE CAST(Id AS NVARCHAR(36)) = @Id", connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@Id", id);
                    var count = (int)await checkCmd.ExecuteScalarAsync();
                    if (count == 0)
                    {
                        transaction.Rollback();
                        return Results.NotFound(new { message = "Usuario no encontrado" });
                    }
                }

                // Desasignar historias
                using (var unassignStoriesCmd = new SqlCommand(@"
                    UPDATE UserStories SET AssigneeId = NULL 
                    WHERE CAST(AssigneeId AS NVARCHAR(36)) = @UserId", connection, transaction))
                {
                    unassignStoriesCmd.Parameters.AddWithValue("@UserId", id);
                    await unassignStoriesCmd.ExecuteNonQueryAsync();
                }

                // Desasignar tareas
                using (var unassignTasksCmd = new SqlCommand(@"
                    UPDATE Tasks SET AssignedToId = NULL 
                    WHERE CAST(AssignedToId AS NVARCHAR(36)) = @UserId", connection, transaction))
                {
                    unassignTasksCmd.Parameters.AddWithValue("@UserId", id);
                    await unassignTasksCmd.ExecuteNonQueryAsync();
                }

                // Eliminar datos relacionados
                var deleteCommands = new[]
                {
                    "DELETE FROM StandupNotes WHERE CAST(UserId AS NVARCHAR(36)) = @UserId",
                    "DELETE FROM Notifications WHERE CAST(UserId AS NVARCHAR(36)) = @UserId OR CAST(CreatorId AS NVARCHAR(36)) = @UserId",
                    "DELETE FROM ProjectMembers WHERE CAST(UserId AS NVARCHAR(36)) = @UserId",
                    "DELETE FROM Users WHERE CAST(Id AS NVARCHAR(36)) = @UserId"
                };

                foreach (var sql in deleteCommands)
                {
                    using var cmd = new SqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@UserId", id);
                    await cmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                return Results.Ok(new { message = "Usuario eliminado exitosamente" });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Results.Problem($"Error al eliminar usuario: {ex.Message}");
            }
        });
    }

    public static UserDto ToUserDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt
        };
    }

    public static string BuildAvatar(string name)
    {
        var initials = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]))
            .Take(2)
            .ToArray();

        return initials.Length == 0 ? "U" : new string(initials);
    }

    public static string BuildAvatarColor()
    {
        var colors = new[] { "#EF4444", "#F97316", "#F59E0B", "#84CC16", "#10B981", "#06B6D4", "#3B82F6", "#6366F1", "#8B5CF6", "#A855F7", "#EC4899", "#F43F5E" };
        var random = new Random();
        return colors[random.Next(colors.Length)];
    }

    private static UserRole ParseRole(string role)
    {
        return Enum.TryParse<UserRole>(role, true, out var parsed) ? parsed : UserRole.Developer;
    }
}
