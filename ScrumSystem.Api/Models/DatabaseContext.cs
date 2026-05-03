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
            using var cmd = new SqlCommand(command, connection);
            cmd.ExecuteNonQuery();
        }

        // Insert default users if not exists
        SeedData(connection);
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
            ProductOwnerId UNIQUEIDENTIFIER REFERENCES Users(Id),
            ScrumMasterId UNIQUEIDENTIFIER REFERENCES Users(Id),
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectMembers')
        CREATE TABLE ProjectMembers (
            ProjectId UNIQUEIDENTIFIER REFERENCES Projects(Id),
            UserId UNIQUEIDENTIFIER REFERENCES Users(Id),
            JoinedAt DATETIME2 DEFAULT GETDATE(),
            PRIMARY KEY (ProjectId, UserId)
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sprints')
        CREATE TABLE Sprints (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            ProjectId UNIQUEIDENTIFIER NOT NULL REFERENCES Projects(Id),
            Name NVARCHAR(100) NOT NULL,
            Goal NVARCHAR(500),
            StartDate DATE NOT NULL,
            EndDate DATE NOT NULL,
            Status NVARCHAR(20) DEFAULT 'Planning' CHECK (Status IN ('Planning', 'Active', 'Completed', 'Cancelled')),
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserStories')
        CREATE TABLE UserStories (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            ProjectId UNIQUEIDENTIFIER NOT NULL REFERENCES Projects(Id),
            SprintId UNIQUEIDENTIFIER REFERENCES Sprints(Id),
            Title NVARCHAR(200) NOT NULL,
            Description NVARCHAR(2000),
            AcceptanceCriteria NVARCHAR(1000),
            StoryPoints INT,
            Priority INT DEFAULT 0,
            Status NVARCHAR(20) DEFAULT 'Backlog' CHECK (Status IN ('Backlog', 'SprintBacklog', 'InProgress', 'Done', 'Cancelled')),
            CreatedBy UNIQUEIDENTIFIER REFERENCES Users(Id),
            CreatedAt DATETIME2 DEFAULT GETDATE()
        )",

        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Tasks')
        CREATE TABLE Tasks (
            Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
            StoryId UNIQUEIDENTIFIER NOT NULL REFERENCES UserStories(Id),
            Title NVARCHAR(200) NOT NULL,
            Description NVARCHAR(1000),
            EstimatedHours INT,
            ActualHours INT DEFAULT 0,
            Status NVARCHAR(20) DEFAULT 'Todo' CHECK (Status IN ('Todo', 'InProgress', 'Done', 'Blocked')),
            AssignedTo UNIQUEIDENTIFIER REFERENCES Users(Id),
            CreatedAt DATETIME2 DEFAULT GETDATE(),
            CompletedAt DATETIME2
        )",

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
        )"
    };
}
