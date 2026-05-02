using System.IdentityModel.Tokens.Jwt;
using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class AuthRoutes
{
    public static void MapAuthRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/google", (GoogleAuthRequest request, AppDataStore store) =>
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

                lock (store.SyncRoot)
                {
                    var user = store.Data.Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                    if (user is null)
                    {
                        user = new User
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name,
                            Email = email,
                            PasswordHash = "google_auth",
                            Role = UserRole.Developer,
                            Avatar = UserRoutes.BuildAvatar(string.IsNullOrWhiteSpace(name) ? email : name),
                            CreatedAt = DateTime.UtcNow
                        };

                        store.Data.Users.Add(user);
                        store.Save();
                    }

                    return Results.Ok(UserRoutes.ToUserDto(user));
                }
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
