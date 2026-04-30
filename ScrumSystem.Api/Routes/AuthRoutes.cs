using Microsoft.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class AuthRoutes
{
    public static void MapAuthRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // Google Sign In
        group.MapPost("/google", async (GoogleAuthRequest request, DatabaseContext db) =>
        {
            try
            {
                Console.WriteLine($"[GOOGLE AUTH] Received credential: {request.Credential?.Substring(0, 50)}...");
                
                // Verify Google token (simplified - in production use Google API client library)
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(request.Credential);
                
                Console.WriteLine($"[GOOGLE AUTH] Token claims: {string.Join(", ", jwtToken.Claims.Select(c => $"{c.Type}={c.Value}"))}");
                
                var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
                var name = jwtToken.Claims.FirstOrDefault(c => c.Type == "name")?.Value;
                var picture = jwtToken.Claims.FirstOrDefault(c => c.Type == "picture")?.Value;
                
                if (string.IsNullOrEmpty(email))
                {
                    return Results.BadRequest("Invalid Google token");
                }

                using var conn = db.CreateConnection();
                await conn.OpenAsync();

                // Check if user exists
                User? user = null;
                var checkSql = "SELECT * FROM Users WHERE Email = @Email";
                using (var cmd = new SqlCommand(checkSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", email);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        user = MapUser(reader);
                    }
                }

                // Create user if not exists
                if (user == null)
                {
                    var id = Guid.NewGuid();
                    var insertSql = @"
                        INSERT INTO Users (Id, Name, Email, PasswordHash, Role, CreatedAt)
                        VALUES (@Id, @Name, @Email, @PasswordHash, @Role, @CreatedAt)";
                    
                    using var cmd = new SqlCommand(insertSql, conn);
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@Name", name ?? email.Split('@')[0]);
                    cmd.Parameters.AddWithValue("@Email", email);
                    cmd.Parameters.AddWithValue("@PasswordHash", "google_auth"); // Placeholder for Google users
                    cmd.Parameters.AddWithValue("@Role", UserRole.Developer.ToString());
                    cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
                    await cmd.ExecuteNonQueryAsync();

                    user = new User
                    {
                        Id = id,
                        Name = name ?? email.Split('@')[0],
                        Email = email,
                        Role = UserRole.Developer,
                        CreatedAt = DateTime.UtcNow
                    };
                }

                return Results.Ok(new UserDto
                {
                    Id = user.Id,
                    Name = user.Name,
                    Email = user.Email,
                    Role = user.Role,
                    CreatedAt = user.CreatedAt
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GOOGLE AUTH ERROR] {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[GOOGLE AUTH ERROR] Stack: {ex.StackTrace}");
                return Results.Problem($"Error during Google authentication: {ex.Message}");
            }
        });
    }

    private static User MapUser(SqlDataReader reader)
    {
        return new User
        {
            Id = (Guid)reader["Id"],
            Name = reader["Name"].ToString()!,
            Email = reader["Email"].ToString()!,
            Role = Enum.Parse<UserRole>(reader["Role"].ToString()!),
            CreatedAt = (DateTime)reader["CreatedAt"]
        };
    }
}

public class GoogleAuthRequest
{
    public string Credential { get; set; } = "";
}
