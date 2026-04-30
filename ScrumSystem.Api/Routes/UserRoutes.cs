using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class UserRoutes
{
    public static void MapUserRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        // Register user (public endpoint)
        group.MapPost("/register", async (RegisterRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Check if email already exists
                var checkSql = "SELECT COUNT(*) FROM Users WHERE Email = @Email";
                using var checkCmd = new SqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@Email", request.Email);
                var count = (int)await checkCmd.ExecuteScalarAsync();

                if (count > 0)
                {
                    return Results.Problem("El email ya está registrado", statusCode: 400);
                }

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                var id = Guid.NewGuid();

                var sql = @"
                    INSERT INTO Users (Id, Name, Email, PasswordHash, Role, CreatedAt) 
                    VALUES (@Id, @Name, @Email, @PasswordHash, @Role, @CreatedAt)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Name", request.Name);
                cmd.Parameters.AddWithValue("@Email", request.Email);
                cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                cmd.Parameters.AddWithValue("@Role", "Developer");
                cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

                await cmd.ExecuteNonQueryAsync();

                // Simulate sending welcome email
                Console.WriteLine($"[EMAIL] Welcome email sent to: {request.Email}");
                Console.WriteLine($"[EMAIL] Subject: ¡Bienvenido a Scrum System!");
                Console.WriteLine($"[EMAIL] Body: Hola {request.Name}, tu cuenta ha sido creada exitosamente.");

                return Results.Ok(new { message = "Usuario registrado exitosamente", userId = id });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error al registrar usuario: {ex.Message}");
            }
        });

        // Create user
        group.MapPost("/", async (CreateUserRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                var id = Guid.NewGuid();

                var sql = @"
                    INSERT INTO Users (Id, Name, Email, PasswordHash, Role) 
                    VALUES (@Id, @Name, @Email, @PasswordHash, @Role)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Name", request.Name);
                cmd.Parameters.AddWithValue("@Email", request.Email);
                cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                cmd.Parameters.AddWithValue("@Role", request.Role.ToString());

                await cmd.ExecuteNonQueryAsync();

                return Results.Created($"/api/users/{id}", new UserDto
                {
                    Id = id,
                    Name = request.Name,
                    Email = request.Email,
                    Role = request.Role,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                return Results.Problem("Email already exists", statusCode: 400);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating user: {ex.Message}");
            }
        });

        // Login
        group.MapPost("/login", async (LoginRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = "SELECT * FROM Users WHERE Email = @Email";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Email", request.Email);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return Results.Unauthorized();
                }

                var storedHash = reader["PasswordHash"].ToString()!;
                if (!BCrypt.Net.BCrypt.Verify(request.Password, storedHash))
                {
                    return Results.Unauthorized();
                }

                var user = new UserDto
                {
                    Id = (Guid)reader["Id"],
                    Name = reader["Name"].ToString()!,
                    Email = reader["Email"].ToString()!,
                    Role = Enum.Parse<UserRole>(reader["Role"].ToString()!),
                    CreatedAt = (DateTime)reader["CreatedAt"]
                };

                return Results.Ok(user);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Login error: {ex.Message}");
            }
        });

        // Get all users
        group.MapGet("/", async (DatabaseContext db) =>
        {
            var users = new List<UserDto>();

            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT Id, Name, Email, Role, CreatedAt 
                FROM Users 
                ORDER BY Name";

            using var cmd = new SqlCommand(sql, conn);
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

        // Get user by ID
        group.MapGet("/{id:guid}", async (Guid id, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT Id, Name, Email, Role, CreatedAt 
                FROM Users 
                WHERE Id = @Id";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.NotFound();
            }

            return Results.Ok(new UserDto
            {
                Id = (Guid)reader["Id"],
                Name = reader["Name"].ToString()!,
                Email = reader["Email"].ToString()!,
                Role = Enum.Parse<UserRole>(reader["Role"].ToString()!),
                CreatedAt = (DateTime)reader["CreatedAt"]
            });
        });

        // Search user by email
        group.MapGet("/search", async (string email, DatabaseContext db) =>
        {
            using var conn = db.CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                SELECT Id, Name, Email, Role, CreatedAt 
                FROM Users 
                WHERE Email = @Email";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Email", email);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.NotFound();
            }

            return Results.Ok(new UserDto
            {
                Id = (Guid)reader["Id"],
                Name = reader["Name"].ToString()!,
                Email = reader["Email"].ToString()!,
                Role = Enum.Parse<UserRole>(reader["Role"].ToString()!),
                CreatedAt = (DateTime)reader["CreatedAt"]
            });
        });

        // Update user
        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = @"
                    UPDATE Users 
                    SET Name = @Name, Email = @Email 
                    WHERE Id = @Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Name", request.Name);
                cmd.Parameters.AddWithValue("@Email", request.Email);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected == 0)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new { message = "Usuario actualizado exitosamente" });
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                return Results.Problem("El email ya está en uso", statusCode: 400);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error al actualizar usuario: {ex.Message}");
            }
        });

        // Change password
        group.MapPut("/{id:guid}/password", async (Guid id, ChangePasswordRequest request, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Verify current password
                var verifySql = "SELECT PasswordHash FROM Users WHERE Id = @Id";
                using var verifyCmd = new SqlCommand(verifySql, conn);
                verifyCmd.Parameters.AddWithValue("@Id", id);
                var storedHash = await verifyCmd.ExecuteScalarAsync();

                if (storedHash == null || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, storedHash.ToString()))
                {
                    return Results.Problem("Contraseña actual incorrecta", statusCode: 400);
                }

                // Update password
                var newHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                var updateSql = "UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @Id";
                using var updateCmd = new SqlCommand(updateSql, conn);
                updateCmd.Parameters.AddWithValue("@Id", id);
                updateCmd.Parameters.AddWithValue("@PasswordHash", newHash);
                await updateCmd.ExecuteNonQueryAsync();

                return Results.Ok(new { message = "Contraseña cambiada exitosamente" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error al cambiar contraseña: {ex.Message}");
            }
        });

        // Delete user
        group.MapDelete("/{id:guid}", async (Guid id, DatabaseContext db) =>
        {
            try
            {
                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                var sql = "DELETE FROM Users WHERE Id = @Id";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected == 0)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new { message = "Usuario eliminado exitosamente" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error al eliminar usuario: {ex.Message}");
            }
        });
    }
}
