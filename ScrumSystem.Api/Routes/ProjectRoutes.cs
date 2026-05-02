using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class ProjectRoutes
{
    public static void MapProjectRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects");

        group.MapGet("/", (string? userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var visibleProjects = store.Data.Projects
                    .Where(project => string.IsNullOrWhiteSpace(userId) || store.Data.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == userId))
                    .OrderByDescending(project => project.CreatedAt)
                    .Select(project => ToProjectDto(project, store))
                    .ToList();

                return Results.Ok(visibleProjects);
            }
        });

        group.MapGet("/{id}", (string id, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var project = store.Data.Projects.FirstOrDefault(p => p.Id == id);
                return project is null ? Results.NotFound() : Results.Ok(ToProjectDto(project, store));
            }
        });

        group.MapPost("/", (CreateProjectRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                if (string.IsNullOrWhiteSpace(request.CreatedById))
                {
                    return Results.BadRequest("El proyecto requiere un creador válido");
                }

                var creator = store.Data.Users.FirstOrDefault(user => user.Id == request.CreatedById);
                if (creator is null)
                {
                    return Results.BadRequest("El usuario creador no existe");
                }

                var project = new Project
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = request.Name.Trim(),
                    Description = request.Description?.Trim(),
                    Key = string.IsNullOrWhiteSpace(request.Key) ? BuildProjectKey(request.Name) : request.Key.Trim().ToUpperInvariant(),
                    Color = request.Color,
                    Icon = request.Icon,
                    CreatorId = creator.Id,
                    CreatedAt = DateTime.UtcNow
                };

                store.Data.Projects.Add(project);
                AddMember(project.Id, creator.Id, "Owner", store);

                foreach (var memberId in request.MemberIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct() ?? Enumerable.Empty<string>())
                {
                    if (memberId == creator.Id || store.Data.Users.All(user => user.Id != memberId))
                    {
                        continue;
                    }

                    AddMember(project.Id, memberId, "Developer", store);
                    CreateNotification(
                        store,
                        userId: memberId,
                        title: "Te agregaron a un proyecto",
                        message: $"Ahora formas parte del proyecto {project.Name}.",
                        type: "project_member_added",
                        projectId: project.Id,
                        creatorId: creator.Id,
                        status: "accepted");
                }

                store.Save();
                return Results.Created($"/api/projects/{project.Id}", ToProjectDto(project, store));
            }
        });

        group.MapPost("/{id}/members", (string id, AddProjectMemberRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var project = store.Data.Projects.FirstOrDefault(p => p.Id == id);
                var user = store.Data.Users.FirstOrDefault(u => u.Id == request.UserId);
                if (project is null || user is null)
                {
                    return Results.NotFound();
                }

                // Verificar si ya es miembro
                if (store.Data.ProjectMembers.Any(member => member.ProjectId == id && member.UserId == request.UserId))
                {
                    return Results.Ok(new { message = "El usuario ya pertenece al proyecto" });
                }

                // Verificar si ya tiene una invitación pendiente
                if (store.Data.ProjectInvitations.Any(inv => inv.ProjectId == id && inv.UserId == request.UserId && inv.Status == "pending"))
                {
                    return Results.Ok(new { message = "Ya existe una invitación pendiente para este usuario" });
                }

                // Crear invitación pendiente
                var invitation = new ProjectInvitation
                {
                    Id = Guid.NewGuid().ToString(),
                    ProjectId = id,
                    UserId = request.UserId,
                    InvitedById = project.CreatorId,
                    Role = "Developer",
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow
                };
                store.Data.ProjectInvitations.Add(invitation);

                // Crear notificación para el usuario invitado
                CreateNotification(
                    store,
                    userId: request.UserId,
                    title: "Invitación a proyecto",
                    message: $"Has sido invitado a unirte al proyecto '{project.Name}'.",
                    type: "project_invitation",
                    projectId: id,
                    creatorId: project.CreatorId,
                    status: "pending");

                store.Save();
                return Results.Ok(new { message = "Invitación enviada correctamente", invitationId = invitation.Id });
            }
        });

        // Aceptar invitación
        group.MapPost("/invitations/{invitationId}/accept", (string invitationId, string userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var invitation = store.Data.ProjectInvitations.FirstOrDefault(i => i.Id == invitationId && i.UserId == userId);
                if (invitation is null)
                {
                    return Results.NotFound(new { message = "Invitación no encontrada" });
                }

                if (invitation.Status != "pending")
                {
                    return Results.BadRequest(new { message = "La invitación ya fue respondida" });
                }

                var project = store.Data.Projects.FirstOrDefault(p => p.Id == invitation.ProjectId);
                if (project is null)
                {
                    return Results.NotFound(new { message = "Proyecto no encontrado" });
                }

                // Actualizar invitación
                invitation.Status = "accepted";
                invitation.RespondedAt = DateTime.UtcNow;

                // Agregar como miembro
                AddMember(invitation.ProjectId, invitation.UserId, invitation.Role, store);

                // Crear notificación al creador
                CreateNotification(
                    store,
                    userId: invitation.InvitedById,
                    title: "Invitación aceptada",
                    message: $"El usuario ha aceptado unirse al proyecto '{project.Name}'.",
                    type: "project_invitation_accepted",
                    projectId: invitation.ProjectId,
                    creatorId: userId,
                    status: "accepted");

                store.Save();
                return Results.Ok(new { message = "Invitación aceptada. Ahora eres miembro del proyecto." });
            }
        });

        // Rechazar invitación
        group.MapPost("/invitations/{invitationId}/reject", (string invitationId, string userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var invitation = store.Data.ProjectInvitations.FirstOrDefault(i => i.Id == invitationId && i.UserId == userId);
                if (invitation is null)
                {
                    return Results.NotFound(new { message = "Invitación no encontrada" });
                }

                if (invitation.Status != "pending")
                {
                    return Results.BadRequest(new { message = "La invitación ya fue respondida" });
                }

                var project = store.Data.Projects.FirstOrDefault(p => p.Id == invitation.ProjectId);

                // Actualizar invitación
                invitation.Status = "rejected";
                invitation.RespondedAt = DateTime.UtcNow;

                // Crear notificación al creador
                CreateNotification(
                    store,
                    userId: invitation.InvitedById,
                    title: "Invitación rechazada",
                    message: $"El usuario ha rechazado la invitación al proyecto '{project?.Name ?? "desconocido"}'.",
                    type: "project_invitation_rejected",
                    projectId: invitation.ProjectId,
                    creatorId: userId,
                    status: "rejected");

                store.Save();
                return Results.Ok(new { message = "Invitación rechazada" });
            }
        });

        // Listar invitaciones pendientes del usuario
        group.MapGet("/invitations/pending", (string userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var invitations = store.Data.ProjectInvitations
                    .Where(i => i.UserId == userId && i.Status == "pending")
                    .Join(store.Data.Projects, inv => inv.ProjectId, p => p.Id, (inv, project) => new
                    {
                        inv.Id,
                        inv.ProjectId,
                        inv.UserId,
                        inv.InvitedById,
                        inv.Role,
                        inv.Status,
                        inv.CreatedAt,
                        ProjectName = project.Name,
                        ProjectKey = project.Key
                    })
                    .OrderByDescending(i => i.CreatedAt)
                    .ToList();

                return Results.Ok(invitations);
            }
        });

        // Listar invitaciones enviadas por el creador (para un proyecto)
        group.MapGet("/{id}/invitations", (string id, string? userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var project = store.Data.Projects.FirstOrDefault(p => p.Id == id);
                if (project is null)
                {
                    return Results.NotFound();
                }

                // Solo el creador puede ver las invitaciones
                if (!string.IsNullOrWhiteSpace(userId) && project.CreatorId != userId)
                {
                    return Results.BadRequest(new { message = "Solo el creador puede ver las invitaciones" });
                }

                var invitations = store.Data.ProjectInvitations
                    .Where(i => i.ProjectId == id)
                    .Join(store.Data.Users, inv => inv.UserId, u => u.Id, (inv, user) => new
                    {
                        inv.Id,
                        inv.ProjectId,
                        UserId = inv.UserId,
                        UserName = user.Name,
                        UserEmail = user.Email,
                        inv.InvitedById,
                        inv.Role,
                        inv.Status,
                        inv.CreatedAt,
                        inv.RespondedAt
                    })
                    .OrderByDescending(i => i.CreatedAt)
                    .ToList();

                return Results.Ok(invitations);
            }
        });

        group.MapPost("/{id}/leave", (string id, string userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var project = store.Data.Projects.FirstOrDefault(p => p.Id == id);
                if (project is null)
                {
                    return Results.NotFound();
                }

                if (project.CreatorId == userId)
                {
                    return Results.BadRequest("El creador no puede salir del proyecto. Debe eliminarlo o transferir la propiedad.");
                }

                var removed = store.Data.ProjectMembers.RemoveAll(member => member.ProjectId == id && member.UserId == userId);
                if (removed == 0)
                {
                    return Results.NotFound();
                }

                store.Save();
                return Results.Ok(new { message = "Has salido del proyecto" });
            }
        });

        group.MapPut("/{id}", (string id, UpdateProjectRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var project = store.Data.Projects.FirstOrDefault(p => p.Id == id);
                if (project is null)
                {
                    return Results.NotFound();
                }

                if (!string.IsNullOrWhiteSpace(request.UserId) && project.CreatorId != request.UserId)
                {
                    return Results.BadRequest("Solo el creador puede actualizar el proyecto");
                }

                project.Name = request.Name.Trim();
                project.Description = request.Description?.Trim() ?? project.Description;
                project.Key = string.IsNullOrWhiteSpace(request.Key) ? project.Key : request.Key.Trim().ToUpperInvariant();
                project.Color = request.Color;
                project.Icon = request.Icon;
                project.UpdatedAt = DateTime.UtcNow;

                store.Save();
                return Results.Ok(new { message = "Proyecto actualizado" });
            }
        });

        group.MapDelete("/{id}", (string id, string? userId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var project = store.Data.Projects.FirstOrDefault(p => p.Id == id);
                if (project is null)
                {
                    return Results.NotFound();
                }

                if (!string.IsNullOrWhiteSpace(userId) && project.CreatorId != userId)
                {
                    return Results.BadRequest("Solo el creador puede eliminar el proyecto");
                }

                var sprintIds = store.Data.Sprints.Where(sprint => sprint.ProjectId == id).Select(sprint => sprint.Id).ToHashSet();
                var storyIds = store.Data.UserStories.Where(story => story.ProjectId == id).Select(story => story.Id).ToHashSet();

                store.Data.Projects.Remove(project);
                store.Data.ProjectMembers.RemoveAll(member => member.ProjectId == id);
                store.Data.Sprints.RemoveAll(sprint => sprint.ProjectId == id);
                store.Data.UserStories.RemoveAll(story => story.ProjectId == id);
                store.Data.Tasks.RemoveAll(task => storyIds.Contains(task.StoryId));
                store.Data.StandupNotes.RemoveAll(note => sprintIds.Contains(note.SprintId));
                store.Data.Notifications.RemoveAll(notification => notification.ProjectId == id);

                store.Save();
                return Results.Ok(new { message = "Proyecto eliminado" });
            }
        });
    }

    public static ProjectDto ToProjectDto(Project project, AppDataStore store)
    {
        var creator = store.Data.Users.FirstOrDefault(user => user.Id == project.CreatorId);
        var members = store.Data.ProjectMembers
            .Where(member => member.ProjectId == project.Id)
            .Join(store.Data.Users, member => member.UserId, user => user.Id, (member, user) => new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                CreatedAt = user.CreatedAt
            })
            .OrderBy(user => user.Name)
            .ToList();

        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Key = project.Key,
            Color = project.Color,
            Icon = project.Icon,
            CreatorId = project.CreatorId,
            ProductOwnerId = project.CreatorId,
            CreatorName = creator?.Name,
            CreatedAt = project.CreatedAt,
            Members = members
        };
    }

    public static void CreateNotification(AppDataStore store, string userId, string title, string message, string type, string? projectId, string? creatorId, string status)
    {
        store.Data.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            ProjectId = projectId,
            CreatorId = creatorId,
            Status = status,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void AddMember(string projectId, string userId, string role, AppDataStore store)
    {
        if (store.Data.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == userId))
        {
            return;
        }

        store.Data.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTime.UtcNow
        });
    }

    private static string BuildProjectKey(string name)
    {
        var letters = new string(name
            .Where(char.IsLetterOrDigit)
            .Take(4)
            .ToArray())
            .ToUpperInvariant();

        return string.IsNullOrWhiteSpace(letters) ? "PROJ" : letters;
    }
}
