using ScrumSystem.Api.Data;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Routes;

public static class StandupRoutes
{
    public static void MapStandupRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/standup");

        group.MapPost("/", (CreateStandupRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var note = new StandupNote
                {
                    Id = Guid.NewGuid().ToString(),
                    SprintId = request.SprintId,
                    UserId = request.UserId,
                    Date = request.Date.Date,
                    Yesterday = request.Yesterday?.Trim(),
                    Today = request.Today?.Trim(),
                    Blockers = request.Blockers?.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                store.Data.StandupNotes.Add(note);
                store.Save();
                return Results.Created($"/api/standup/{note.Id}", note);
            }
        });

        group.MapGet("/sprint/{sprintId}", (string sprintId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                return Results.Ok(store.Data.StandupNotes
                    .Where(note => note.SprintId == sprintId)
                    .OrderByDescending(note => note.Date)
                    .Select(note => ToStandupDto(note, store))
                    .ToList());
            }
        });

        group.MapGet("/sprint/{sprintId}/today", (string sprintId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var today = DateTime.Today;
                return Results.Ok(store.Data.StandupNotes
                    .Where(note => note.SprintId == sprintId && note.Date.Date == today)
                    .OrderBy(note => note.CreatedAt)
                    .Select(note => ToStandupDto(note, store))
                    .ToList());
            }
        });

        group.MapPatch("/{id}", (string id, CreateStandupRequest request, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var note = store.Data.StandupNotes.FirstOrDefault(item => item.Id == id);
                if (note is null)
                {
                    return Results.NotFound();
                }

                note.Yesterday = request.Yesterday?.Trim();
                note.Today = request.Today?.Trim();
                note.Blockers = request.Blockers?.Trim();
                store.Save();
                return Results.Ok(new { message = "Note updated" });
            }
        });

        group.MapGet("/sprint/{sprintId}/missing", (string sprintId, AppDataStore store) =>
        {
            lock (store.SyncRoot)
            {
                var sprint = store.Data.Sprints.FirstOrDefault(item => item.Id == sprintId);
                if (sprint is null)
                {
                    return Results.NotFound();
                }

                var today = DateTime.Today;
                var completedUserIds = store.Data.StandupNotes
                    .Where(note => note.SprintId == sprintId && note.Date.Date == today)
                    .Select(note => note.UserId)
                    .ToHashSet();

                var users = store.Data.ProjectMembers
                    .Where(member => member.ProjectId == sprint.ProjectId && !completedUserIds.Contains(member.UserId))
                    .Join(store.Data.Users, member => member.UserId, user => user.Id, (_, user) => UserRoutes.ToUserDto(user))
                    .OrderBy(user => user.Name)
                    .ToList();

                return Results.Ok(users);
            }
        });
    }

    private static StandupNoteDto ToStandupDto(StandupNote note, AppDataStore store)
    {
        var user = store.Data.Users.FirstOrDefault(item => item.Id == note.UserId);
        return new StandupNoteDto
        {
            Id = note.Id,
            SprintId = note.SprintId,
            UserId = note.UserId,
            Date = note.Date,
            Yesterday = note.Yesterday,
            Today = note.Today,
            Blockers = note.Blockers,
            CreatedAt = note.CreatedAt,
            UserName = user?.Name
        };
    }
}
