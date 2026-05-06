using Microsoft.Data.SqlClient;

namespace ScrumSystem.Api.Models;

public class DatabaseContext
{
    private readonly string _connectionString;

    public DatabaseContext(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    public SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    public void Initialize()
    {
        try
        {
            // First, ensure database exists using master connection
            var masterConnectionString = _connectionString.Replace("Database=ScrumSystem", "Database=master");
            using (var masterConn = new SqlConnection(masterConnectionString))
            {
                masterConn.Open();
                var createDbSql = @"
                    IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'ScrumSystem')
                    CREATE DATABASE ScrumSystem";
                using var cmd = new SqlCommand(createDbSql, masterConn);
                cmd.ExecuteNonQuery();
            }

            // Now create tables in ScrumSystem database
            using var connection = CreateConnection();
            connection.Open();

            var commands = GetCreateTableCommands();
            foreach (var command in commands)
            {
                try
                {
                    using var cmd = new SqlCommand(command, connection);
                    cmd.ExecuteNonQuery();
                }
                catch (SqlException ex) when (ex.Number == 2714) // Table already exists
                {
                    // Table already exists, continue
                }
                catch (SqlException ex)
                {
                    // Log error but continue with other commands
                    Console.WriteLine($"SQL Error {ex.Number}: {ex.Message}");
                    Console.WriteLine($"Command: {command.Substring(0, Math.Min(100, command.Length))}...");
                }
            }

            // Insert default users if not exists
            SeedData(connection);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database initialization warning: {ex.Message}");
            // Continue even if there's an error
        }
    }

    private void SeedData(SqlConnection connection)
    {
        var checkUser = "SELECT COUNT(*) FROM Users WHERE Email = 'admin@scrum.com'";
        using var checkCmd = new SqlCommand(checkUser, connection);
        var count = (int)checkCmd.ExecuteScalar();

        if (count == 0)
        {
            var passwordHash = BCrypt.Net.BCrypt.HashPassword("admin123");
            var insertUsers = @"
                INSERT INTO Users (Id, Name, Email, PasswordHash, Role, CreatedAt) VALUES 
                (NEWID(), 'Admin User', 'admin@scrum.com', @password, 'ProductOwner', GETDATE()),
                (NEWID(), 'Scrum Master', 'scrum@scrum.com', @password, 'ScrumMaster', GETDATE()),
                (NEWID(), 'Developer 1', 'dev1@scrum.com', @password, 'Developer', GETDATE()),
                (NEWID(), 'Developer 2', 'dev2@scrum.com', @password, 'Developer', GETDATE())";
            
            using var cmd = new SqlCommand(insertUsers, connection);
            cmd.Parameters.AddWithValue("@password", passwordHash);
            cmd.ExecuteNonQuery();
        }
    }

    private static string[] GetCreateTableCommands() => new[]
    {
        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
        CREATE TABLE Users (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            Name NVARCHAR(100) NOT NULL,
            Email NVARCHAR(100) UNIQUE NOT NULL,
            PasswordHash NVARCHAR(200) NOT NULL,
            Role NVARCHAR(20) NOT NULL CHECK (Role IN ('ProductOwner', 'ScrumMaster', 'Developer')),
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Projects')
        CREATE TABLE Projects (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            Name NVARCHAR(100) NOT NULL,
            Description NVARCHAR(500),
            Key NVARCHAR(10),
            Color NVARCHAR(7),
            Icon NVARCHAR(50),
            CreatorId UNIQUEIDENTIFIER REFERENCES Users(Id),
            ProductOwnerId UNIQUEIDENTIFIER REFERENCES Users(Id),
            ScrumMasterId UNIQUEIDENTIFIER REFERENCES Users(Id),
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Projects') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'Key' AND object_id = OBJECT_ID('Projects'))
        ALTER TABLE Projects ADD [Key] NVARCHAR(10)",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Projects') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'Color' AND object_id = OBJECT_ID('Projects'))
        ALTER TABLE Projects ADD Color NVARCHAR(7)",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Projects') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'Icon' AND object_id = OBJECT_ID('Projects'))
        ALTER TABLE Projects ADD Icon NVARCHAR(50)",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Projects') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'CreatorId' AND object_id = OBJECT_ID('Projects'))
        ALTER TABLE Projects ADD CreatorId UNIQUEIDENTIFIER REFERENCES Users(Id)",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectMembers')
        CREATE TABLE ProjectMembers (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            ProjectId UNIQUEIDENTIFIER REFERENCES Projects(Id),
            UserId UNIQUEIDENTIFIER REFERENCES Users(Id),
            Role NVARCHAR(50) DEFAULT 'Developer',
            JoinedAt DATETIME2 DEFAULT GETDATE()
        )",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectMembers') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'Role' AND object_id = OBJECT_ID('ProjectMembers'))
        ALTER TABLE ProjectMembers ADD Role NVARCHAR(50) DEFAULT 'Developer'",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectMembers') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'Id' AND object_id = OBJECT_ID('ProjectMembers'))
        ALTER TABLE ProjectMembers ADD Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID()",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectMembers') 
          AND EXISTS (SELECT * FROM sys.key_constraints WHERE name LIKE 'PK__ProjectM%')
        BEGIN
            ALTER TABLE ProjectMembers DROP CONSTRAINT PK__ProjectMembers__ProjectId_UserId;
            ALTER TABLE ProjectMembers ADD CONSTRAINT PK_ProjectMembers PRIMARY KEY (Id);
        END",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sprints')
        CREATE TABLE Sprints (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            ProjectId UNIQUEIDENTIFIER NOT NULL REFERENCES Projects(Id),
            Name NVARCHAR(100) NOT NULL,
            Description NVARCHAR(500),
            Goal NVARCHAR(500),
            StartDate DATE NOT NULL,
            EndDate DATE NOT NULL,
            Status NVARCHAR(20) DEFAULT 'Planning' CHECK (Status IN ('Planning', 'Active', 'Completed', 'Cancelled')),
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Sprints') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'Description' AND object_id = OBJECT_ID('Sprints'))
        ALTER TABLE Sprints ADD Description NVARCHAR(500)",

        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Sprints') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'UpdatedAt' AND object_id = OBJECT_ID('Sprints'))
        ALTER TABLE Sprints ADD UpdatedAt DATETIME2 DEFAULT GETDATE()",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserStories')
        CREATE TABLE UserStories (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            ProjectId UNIQUEIDENTIFIER NOT NULL REFERENCES Projects(Id),
            SprintId UNIQUEIDENTIFIER REFERENCES Sprints(Id),
            Title NVARCHAR(200) NOT NULL,
            Description NVARCHAR(2000),
            AcceptanceCriteria NVARCHAR(1000),
            StoryPoints INT,
            Priority NVARCHAR(20) DEFAULT 'Medium',
            Status NVARCHAR(20) DEFAULT 'Backlog' CHECK (Status IN ('Backlog', 'SprintBacklog', 'InProgress', 'Done', 'Cancelled')),
            [Key] NVARCHAR(50),
            AssigneeId UNIQUEIDENTIFIER REFERENCES Users(Id),
            CreatedBy UNIQUEIDENTIFIER REFERENCES Users(Id),
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'UserStories') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'Key' AND object_id = OBJECT_ID('UserStories'))
        ALTER TABLE UserStories ADD [Key] NVARCHAR(50)",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'UserStories') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'AssigneeId' AND object_id = OBJECT_ID('UserStories'))
        ALTER TABLE UserStories ADD AssigneeId UNIQUEIDENTIFIER REFERENCES Users(Id)",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Tasks')
        CREATE TABLE Tasks (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            StoryId UNIQUEIDENTIFIER NOT NULL REFERENCES UserStories(Id),
            Title NVARCHAR(200) NOT NULL,
            Description NVARCHAR(MAX),
            EstimatedHours INT,
            ActualHours INT DEFAULT 0,
            Status NVARCHAR(20) DEFAULT 'Todo' CHECK (Status IN ('Todo', 'InProgress', 'Done', 'Blocked')),
            AssignedTo UNIQUEIDENTIFIER REFERENCES Users(Id),
            CreatedAt DATETIME2 DEFAULT GETDATE(),
            UpdatedAt DATETIME2 DEFAULT GETDATE(),
            CompletedAt DATETIME2
        )",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Tasks') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'UpdatedAt' AND object_id = OBJECT_ID('Tasks'))
        ALTER TABLE Tasks ADD UpdatedAt DATETIME2 DEFAULT GETDATE()",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Tasks') 
          AND EXISTS (SELECT * FROM sys.columns WHERE name = 'Description' AND object_id = OBJECT_ID('Tasks') 
                     AND max_length <> -1) -- -1 indicates MAX
        ALTER TABLE Tasks ALTER COLUMN Description NVARCHAR(MAX)",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StandupNotes')
        CREATE TABLE StandupNotes (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            SprintId UNIQUEIDENTIFIER NOT NULL REFERENCES Sprints(Id),
            UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
            Date DATE NOT NULL,
            Yesterday NVARCHAR(500),
            Today NVARCHAR(500),
            Blockers NVARCHAR(500),
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BurndownData')
        CREATE TABLE BurndownData (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            SprintId UNIQUEIDENTIFIER NOT NULL REFERENCES Sprints(Id),
            Date DATE NOT NULL,
            RemainingStoryPoints INT,
            RemainingHours INT,
            IdealRemaining DECIMAL(10,2)
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StoryComments')
        CREATE TABLE StoryComments (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            StoryId UNIQUEIDENTIFIER NOT NULL REFERENCES UserStories(Id),
            UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
            Message NVARCHAR(1000) NOT NULL,
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StoryHistory')
        CREATE TABLE StoryHistory (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            StoryId UNIQUEIDENTIFIER NOT NULL REFERENCES UserStories(Id),
            UserId UNIQUEIDENTIFIER NULL REFERENCES Users(Id),
            EventType NVARCHAR(50) NOT NULL,
            Message NVARCHAR(1000) NOT NULL,
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Notifications')
        CREATE TABLE Notifications (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
            ProjectId UNIQUEIDENTIFIER NULL REFERENCES Projects(Id),
            CreatorId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
            Title NVARCHAR(200) NOT NULL,
            Message NVARCHAR(1000) NOT NULL,
            Type NVARCHAR(50) NOT NULL,
            IsRead BIT DEFAULT 0,
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectInvitations')
        CREATE TABLE ProjectInvitations (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            ProjectId UNIQUEIDENTIFIER NOT NULL REFERENCES Projects(Id),
            UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
            InvitedById UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
            Role NVARCHAR(50) NOT NULL DEFAULT 'Developer',
            Status NVARCHAR(50) NOT NULL DEFAULT 'pending',
            CreatedAt DATETIME2 DEFAULT GETDATE(),
            RespondedAt DATETIME2 NULL
        )",
        @"IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectInvitations') 
          AND NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'RespondedAt' AND object_id = OBJECT_ID('ProjectInvitations'))
        ALTER TABLE ProjectInvitations ADD RespondedAt DATETIME2 NULL"
    };
}
