using System.Text.Json;
using ScrumSystem.Api.Models;

namespace ScrumSystem.Api.Data;

public sealed class AppDataStore
{
    private readonly string _dataPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public object SyncRoot { get; } = new();
    public AppDataSnapshot Data { get; private set; }

    public AppDataStore(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "ScrumSystem", "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _dataPath = Path.Combine(dataDirectory, "scrum-data.json");
        Data = LoadOrCreate();
        EnsureSeedData();
    }

    public void Save()
    {
        lock (SyncRoot)
        {
            var json = JsonSerializer.Serialize(Data, _jsonOptions);
            File.WriteAllText(_dataPath, json);
        }
    }

    private AppDataSnapshot LoadOrCreate()
    {
        if (!File.Exists(_dataPath))
        {
            return new AppDataSnapshot();
        }

        try
        {
            var json = File.ReadAllText(_dataPath);
            return JsonSerializer.Deserialize<AppDataSnapshot>(json, _jsonOptions) ?? new AppDataSnapshot();
        }
        catch
        {
            return new AppDataSnapshot();
        }
    }

    private void EnsureSeedData()
    {
        lock (SyncRoot)
        {
            if (Data.Users.Count > 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            Data.Users.AddRange(new[]
            {
                CreateSeedUser("admin-user", "Admin User", "admin@scrum.com", UserRole.ProductOwner, now),
                CreateSeedUser("scrum-master", "Scrum Master", "scrum@scrum.com", UserRole.ScrumMaster, now),
                CreateSeedUser("developer-1", "Developer 1", "dev1@scrum.com", UserRole.Developer, now),
                CreateSeedUser("developer-2", "Developer 2", "dev2@scrum.com", UserRole.Developer, now)
            });

            Save();
        }
    }

    private static User CreateSeedUser(string id, string name, string email, UserRole role, DateTime createdAt)
    {
        return new User
        {
            Id = id,
            Name = name,
            Email = email,
            Role = role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            Avatar = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]))).Substring(0, Math.Min(2, name.Length)),
            CreatedAt = createdAt
        };
    }
}

public sealed class AppDataSnapshot
{
    public List<User> Users { get; set; } = new();
    public List<Project> Projects { get; set; } = new();
    public List<ProjectMember> ProjectMembers { get; set; } = new();
    public List<Sprint> Sprints { get; set; } = new();
    public List<UserStory> UserStories { get; set; } = new();
    public List<TaskItem> Tasks { get; set; } = new();
    public List<StandupNote> StandupNotes { get; set; } = new();
    public List<Notification> Notifications { get; set; } = new();
    public List<ProjectInvitation> ProjectInvitations { get; set; } = new();
}
