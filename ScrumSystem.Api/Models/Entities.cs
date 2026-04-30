namespace ScrumSystem.Api.Models;

public enum UserRole
{
    ProductOwner,
    ScrumMaster,
    Developer
}

public enum SprintStatus
{
    Planning,
    Active,
    Completed,
    Cancelled
}

public enum StoryStatus
{
    Backlog,
    SprintBacklog,
    InProgress,
    Done,
    Cancelled
}

public enum TaskStatus
{
    Todo,
    InProgress,
    Done,
    Blocked
}

public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Key { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public Guid? ProductOwnerId { get; set; }
    public Guid? ScrumMasterId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProjectDto : Project
{
    public List<UserDto> Members { get; set; } = new();
    public string? CreatorName { get; set; }
}

public class Sprint
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string? Goal { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public SprintStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SprintDto : Sprint
{
    public new string Status { get; set; } = "Planning";
    public int TotalStoryPoints { get; set; }
    public int CompletedStoryPoints { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
}

public class UserStory
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? SprintId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public int? StoryPoints { get; set; }
    public int Priority { get; set; }
    public StoryStatus Status { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserStoryDto : UserStory
{
    public int TaskCount { get; set; }
    public int CompletedTaskCount { get; set; }
    public List<TaskItemDto> Tasks { get; set; } = new();
}

public class BoardStoryDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? StoryPoints { get; set; }
    public int Priority { get; set; }
    public string Status { get; set; } = "";
    public Guid? AssigneeId { get; set; }
    public string? AssigneeName { get; set; }
}

public class BoardDataDto
{
    public List<BoardStoryDto> Stories { get; set; } = new();
    public List<ProjectMemberDto> Members { get; set; } = new();
}

public class ProjectMemberDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
}

public class TaskItem
{
    public Guid Id { get; set; }
    public Guid StoryId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? EstimatedHours { get; set; }
    public int ActualHours { get; set; }
    public TaskStatus Status { get; set; }
    public Guid? AssignedTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class TaskItemDto : TaskItem
{
    public string? AssignedToName { get; set; }
    public string? StoryTitle { get; set; }
}

public class StandupNote
{
    public Guid Id { get; set; }
    public Guid SprintId { get; set; }
    public Guid UserId { get; set; }
    public DateTime Date { get; set; }
    public string? Yesterday { get; set; }
    public string? Today { get; set; }
    public string? Blockers { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StandupNoteDto : StandupNote
{
    public string? UserName { get; set; }
}

public class BurndownData
{
    public int Id { get; set; }
    public Guid SprintId { get; set; }
    public DateTime Date { get; set; }
    public int RemainingStoryPoints { get; set; }
    public int RemainingHours { get; set; }
    public decimal IdealRemaining { get; set; }
}

// Request/Response DTOs
public class CreateProjectRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Key { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public Guid? CreatedById { get; set; }
    public List<Guid>? MemberIds { get; set; }
}

public class UpdateProjectRequest
{
    public string Name { get; set; } = "";
    public string? Key { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public Guid UserId { get; set; }
}

public class CreateSprintRequest
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string? Goal { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class CreateStoryRequest
{
    public Guid ProjectId { get; set; }
    public Guid? SprintId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public int? StoryPoints { get; set; }
    public int Priority { get; set; }
}

public class CreateTaskRequest
{
    public Guid StoryId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? EstimatedHours { get; set; }
}

public class UpdateStatusRequest
{
    public string Status { get; set; } = "";
}

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public Guid? ProjectId { get; set; }
    public string Status { get; set; } = "pending";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NotificationDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public Guid? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? CreatorName { get; set; }
    public string Status { get; set; } = "pending";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class RegisterRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "Developer";
}

public class CreateUserRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public UserRole Role { get; set; }
}

public class UpdateUserRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class CreateStandupRequest
{
    public Guid SprintId { get; set; }
    public Guid UserId { get; set; }
    public DateTime Date { get; set; }
    public string? Yesterday { get; set; }
    public string? Today { get; set; }
    public string? Blockers { get; set; }
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
