using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class UserRoutes
{
    public static void MapUserRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapPost("/register", (RegisterRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                if (store.Data.Users.Any(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.BadRequest("El email ya está registrado");
                }

                var user = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = request.Name.Trim(),
                    Email = request.Email.Trim(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    Role = ParseRole(request.Role),
                    Avatar = BuildAvatar(request.Name),
                    CreatedAt = DateTime.UtcNow
                };

                store.Data.Users.Add(user);
                store.Save();

                return Results.Ok(new { message = "Usuario registrado exitosamente", userId = user.Id });
            }
        });

        group.MapPost("/", (CreateUserRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                if (store.Data.Users.Any(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.BadRequest("Email already exists");
                }

                var user = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = request.Name.Trim(),
                    Email = request.Email.Trim(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    Role = request.Role,
                    Avatar = BuildAvatar(request.Name),
                    CreatedAt = DateTime.UtcNow
                };

                store.Data.Users.Add(user);
                store.Save();
                return Results.Created($"/api/users/{user.Id}", ToUserDto(user));
            }
        });

        group.MapPost("/login", (LoginRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var user = store.Data.Users.FirstOrDefault(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase));
                if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(ToUserDto(user));
            }
        });

        group.MapGet("/", (AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                return Results.Ok(store.Data.Users
                    .OrderBy(u => u.Name)
                    .Select(ToUserDto)
                    .ToList());
            }
        });

        group.MapGet("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var user = store.Data.Users.FirstOrDefault(u => u.Id == id);
                return user is null ? Results.NotFound() : Results.Ok(ToUserDto(user));
            }
        });

        group.MapGet("/search", (string email, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var user = store.Data.Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                return user is null ? Results.NotFound() : Results.Ok(ToUserDto(user));
            }
        });

        group.MapPut("/{id}", (string id, UpdateUserRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var user = store.Data.Users.FirstOrDefault(u => u.Id == id);
                if (user is null)
                {
                    return Results.NotFound();
                }

                if (store.Data.Users.Any(u => u.Id != id && u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.BadRequest("El email ya está en uso");
                }

                user.Name = request.Name.Trim();
                user.Email = request.Email.Trim();
                user.Avatar = BuildAvatar(user.Name);
                user.UpdatedAt = DateTime.UtcNow;
                store.Save();

                return Results.Ok(new { message = "Usuario actualizado exitosamente" });
            }
        });

        group.MapPut("/{id}/password", (string id, ChangePasswordRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var user = store.Data.Users.FirstOrDefault(u => u.Id == id);
                if (user is null)
                {
                    return Results.NotFound();
                }

                if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                {
                    return Results.BadRequest("Contraseña actual incorrecta");
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                user.UpdatedAt = DateTime.UtcNow;
                store.Save();

                return Results.Ok(new { message = "Contraseña cambiada exitosamente" });
            }
        });

        group.MapDelete("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var user = store.Data.Users.FirstOrDefault(u => u.Id == id);
                if (user is null)
                {
                    return Results.NotFound();
                }

                store.Data.Users.Remove(user);
                store.Data.ProjectMembers.RemoveAll(pm => pm.UserId == id);
                store.Data.Notifications.RemoveAll(n => n.UserId == id || n.CreatorId == id);
                store.Data.StandupNotes.RemoveAll(note => note.UserId == id);

                foreach (var story in store.Data.UserStories.Where(story => story.AssigneeId == id))
                {
                    story.AssigneeId = null;
                    story.UpdatedAt = DateTime.UtcNow;
                }

                foreach (var task in store.Data.Tasks.Where(task => task.AssignedToId == id))
                {
                    task.AssignedToId = null;
                }

                store.Save();
                return Results.Ok(new { message = "Usuario eliminado exitosamente" });
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

    private static UserRole ParseRole(string role)
    {
        return Enum.TryParse<UserRole>(role, true, out var parsed) ? parsed : UserRole.Developer;
    }
}
