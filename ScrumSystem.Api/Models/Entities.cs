using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrumSystem.Api.Models;

public enum UserRole
{
    ProductOwner,
    ScrumMaster,
    Developer
}

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Avatar { get; set; }

    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Developer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Key { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public string CreatorId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class ProjectDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Key { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public string CreatorId { get; set; } = string.Empty;
    public string ProductOwnerId { get; set; } = string.Empty;
    public string? CreatorName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<UserDto> Members { get; set; } = new();
}

public class Sprint
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Goal { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DurationWeeks { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string Status { get; set; } = "Planning";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class SprintDto : Sprint
{
    public int TotalStoryPoints { get; set; }
    public int CompletedStoryPoints { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectKey { get; set; }
    public int StoryCount { get; set; }
}

public class UserStory
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = "Backlog";
    public string Priority { get; set; } = "Medium";
    public int? StoryPoints { get; set; }
    public string Type { get; set; } = "Feature";
    public string ProjectId { get; set; } = string.Empty;
    public string? SprintId { get; set; }
    public string? AssigneeId { get; set; }
    public string? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class UserStoryDto : UserStory
{
    public int TaskCount { get; set; }
    public int CompletedTaskCount { get; set; }
    public string? AssigneeName { get; set; }
    public List<TaskItemDto> Tasks { get; set; } = new();
    public List<StoryCommentDto> Comments { get; set; } = new();
    public List<StoryHistoryDto> History { get; set; } = new();
}

public class ProjectMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "Developer";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class BoardStoryDto
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string? SprintId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? StoryPoints { get; set; }
    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = string.Empty;
    public string? AssigneeId { get; set; }
    public string? AssigneeName { get; set; }
}

public class BoardDataDto
{
    public List<BoardStoryDto> Stories { get; set; } = new();
    public List<ProjectMemberDto> Members { get; set; } = new();
    /// <summary>True si el proyecto tiene al menos un sprint en estado Active (tablero Scrum).</summary>
    public bool HasActiveSprint { get; set; }
}

public class ProjectMemberDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StoryId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? EstimatedHours { get; set; }
    public int? ActualHours { get; set; }
    public string Status { get; set; } = "Todo";
    public int Priority { get; set; } = 1;
    public string? AssignedToId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public class TaskItemDto : TaskItem
{
    public string? AssignedToName { get; set; }
    public string? StoryTitle { get; set; }
}

public class StoryComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StoryId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class StoryCommentDto : StoryComment
{
    public string UserName { get; set; } = "Usuario";
}

public class StoryHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StoryId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string EventType { get; set; } = "Update";
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class StoryHistoryDto : StoryHistoryEntry
{
    public string UserName { get; set; } = "Usuario";
}

public class StandupNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SprintId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? Yesterday { get; set; }
    public string? Today { get; set; }
    public string? Blockers { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class StandupNoteDto : StandupNote
{
    public string? UserName { get; set; }
}

public class Notification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string? CreatorId { get; set; }
    public string Status { get; set; } = "pending";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationDto : Notification
{
    public string? ProjectName { get; set; }
    public string? CreatorName { get; set; }
}

public class ProjectInvitation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string InvitedById { get; set; } = string.Empty;
    public string Role { get; set; } = "Developer";
    public string Status { get; set; } = "pending"; // pending, accepted, rejected
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Developer";
}

public class CreateUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}

public class UpdateUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class CreateProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Key { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public string? CreatedById { get; set; }
    public List<string>? MemberIds { get; set; }
}

public class UpdateProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Key { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public string UserId { get; set; } = string.Empty;
}

public class AddProjectMemberRequest
{
    public string UserId { get; set; } = string.Empty;
}

public class CreateSprintRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Status { get; set; }
}

public class CreateStoryRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string? SprintId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public int? StoryPoints { get; set; }
    
    // Accept both int and string for Priority using JsonExtensionData
    [JsonExtensionData]
    public Dictionary<string, object>? ExtensionData { get; set; }
    
    private int _priorityValue = 2;
    public int PriorityValue 
    { 
        get => _priorityValue;
        set 
        {
            _priorityValue = value;
        }
    }
    
    // This property will be set by JSON deserialization
    public object? Priority 
    { 
        get => _priorityValue;
        set 
        {
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Number)
                {
                    _priorityValue = jsonElement.GetInt32();
                }
                else if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    var stringValue = jsonElement.GetString();
                    if (int.TryParse(stringValue, out var intValue))
                    {
                        _priorityValue = intValue;
                    }
                    else
                    {
                        // Handle string values like "Low", "Medium", "High"
                        _priorityValue = stringValue?.ToLowerInvariant() switch
                        {
                            "low" => 1,
                            "high" => 3,
                            _ => 2
                        };
                    }
                }
            }
            else if (value is int intValue)
            {
                _priorityValue = intValue;
            }
            else if (value is string stringValue)
            {
                if (int.TryParse(stringValue, out var stringAsInt))
                {
                    _priorityValue = stringAsInt;
                }
                else
                {
                    _priorityValue = stringValue.ToLowerInvariant() switch
                    {
                        "low" => 1,
                        "high" => 3,
                        _ => 2
                    };
                }
            }
        }
    }
    
    public string? AssigneeId { get; set; }
    public string? Status { get; set; }
}

public class CreateTaskRequest
{
    public string StoryId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? EstimatedHours { get; set; }
    public string? Status { get; set; }
    public string? AssignedToId { get; set; }
    public int Priority { get; set; } = 1;
}

public class CreateStoryCommentRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class UpdateStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class UpdateTaskStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public int ActualHours { get; set; }
}

public class CreateStandupRequest
{
    public string SprintId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? Yesterday { get; set; }
    public string? Today { get; set; }
    public string? Blockers { get; set; }
}

public class CreateNotificationRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string? CreatorId { get; set; }
}

public class MoveStoryRequest
{
    public string? SprintId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DashboardStats
{
    public int TotalProjects { get; set; }
    public int ActiveSprints { get; set; }
    public int TotalStories { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int PendingTasks { get; set; }
}

public class BurndownChartDto
{
    public List<string> Labels { get; set; } = new();
    public List<decimal> Ideal { get; set; } = new();
    public List<decimal> Actual { get; set; } = new();
}
