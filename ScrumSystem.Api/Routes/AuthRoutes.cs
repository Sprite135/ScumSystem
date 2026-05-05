using System.IdentityModel.Tokens.Jwt;
using Microsoft.Data.SqlClient;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class AuthRoutes
{
    public static void MapAuthRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/google", async (GoogleAuthRequest request, DatabaseContext dbContext) =>
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(request.Credential);

                var email = token.Claims.FirstOrDefault(claim => claim.Type == "email")?.Value;
                var name = token.Claims.FirstOrDefault(claim => claim.Type == "name")?.Value;

                if (string.IsNullOrWhiteSpace(email))
                {
                    return Results.BadRequest("Invalid Google token");
                }

                using var connection = dbContext.CreateConnection();
                await connection.OpenAsync();

                // Check if user exists
                var checkSql = "SELECT CAST(Id AS NVARCHAR(36)), Name, Email, Role, CreatedAt FROM Users WHERE Email = @Email";
                User? user = null;
                using (var checkCmd = new SqlCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@Email", email);
                    using var reader = await checkCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        // Role is stored as NVARCHAR in database
                        var roleValue = reader.GetString(3);
                        user = new User
                        {
                            Id = reader.GetString(0),
                            Name = reader.GetString(1),
                            Email = reader.GetString(2),
                            Role = Enum.Parse<UserRole>(roleValue),
                            CreatedAt = reader.GetDateTime(4)
                        };
                    }
                }

                // Create user if not exists
                if (user is null)
                {
                    var userId = Guid.NewGuid();
                    var userName = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name;
                    var avatar = UserRoutes.BuildAvatar(userName);
                    var createdAt = DateTime.UtcNow;

                    var insertSql = @"
                        INSERT INTO Users (Id, Name, Email, PasswordHash, Role, CreatedAt) 
                        VALUES (@Id, @Name, @Email, @PasswordHash, @Role, @CreatedAt)";
                    
                    using var insertCmd = new SqlCommand(insertSql, connection);
                    insertCmd.Parameters.AddWithValue("@Id", userId);
                    insertCmd.Parameters.AddWithValue("@Name", userName);
                    insertCmd.Parameters.AddWithValue("@Email", email);
                    insertCmd.Parameters.AddWithValue("@PasswordHash", "google_auth");
                    insertCmd.Parameters.AddWithValue("@Role", "Developer"); // Role as NVARCHAR
                    insertCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
                    await insertCmd.ExecuteNonQueryAsync();

                    user = new User
                    {
                        Id = userId.ToString(),
                        Name = userName,
                        Email = email,
                        Role = UserRole.Developer,
                        CreatedAt = createdAt
                    };
                }

                return Results.Ok(UserRoutes.ToUserDto(user));
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error during Google authentication: {ex.Message}");
            }
        });
    }
}

public class GoogleAuthRequest
{
    public string Credential { get; set; } = string.Empty;
}
