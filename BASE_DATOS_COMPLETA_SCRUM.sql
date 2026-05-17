-- ========================================
-- BASE DE DATOS COMPLETA - SCRUM SYSTEM
-- ========================================

-- 1. TABLA USERS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
    CREATE TABLE Users (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        Name NVARCHAR(100) NOT NULL,
        Email NVARCHAR(100) UNIQUE NOT NULL,
        PasswordHash NVARCHAR(200) NOT NULL,
        Role NVARCHAR(20) NOT NULL CHECK (Role IN ('ProductOwner', 'ScrumMaster', 'Developer')),
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );

-- 2. TABLA PROJECTS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Projects')
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
    );

-- 3. TABLA PROJECTMEMBERS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectMembers')
    CREATE TABLE ProjectMembers (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        ProjectId UNIQUEIDENTIFIER REFERENCES Projects(Id),
        UserId UNIQUEIDENTIFIER REFERENCES Users(Id),
        Role NVARCHAR(50) DEFAULT 'Developer',
        JoinedAt DATETIME2 DEFAULT GETDATE()
    );

-- 4. TABLA SPRINTS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sprints')
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
    );

-- 5. TABLA USERSTORIES
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserStories')
    CREATE TABLE UserStories (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        ProjectId UNIQUEIDENTIFIER NOT NULL REFERENCES Projects(Id),
        SprintId UNIQUEIDENTIFIER REFERENCES Sprints(Id),
        Title NVARCHAR(200) NOT NULL,
        Description NVARCHAR(2000),
        AcceptanceCriteria NVARCHAR(1000),
        StoryPoints INT,
        Priority NVARCHAR(20) DEFAULT 'Medium' CHECK (Priority IN ('Low', 'Medium', 'High', 'Critical')),
        Status NVARCHAR(20) DEFAULT 'Backlog' CHECK (Status IN ('Backlog', 'SprintBacklog', 'InProgress', 'Done', 'Cancelled')),
        [Key] NVARCHAR(50),
        AssigneeId UNIQUEIDENTIFIER REFERENCES Users(Id),
        CreatedBy UNIQUEIDENTIFIER REFERENCES Users(Id),
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );

-- 6. TABLA TASKS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Tasks')
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
    );

-- 7. TABLA STANDUPNOTES
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StandupNotes')
    CREATE TABLE StandupNotes (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        SprintId UNIQUEIDENTIFIER NOT NULL REFERENCES Sprints(Id),
        UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
        Date DATE NOT NULL,
        Yesterday NVARCHAR(500),
        Today NVARCHAR(500),
        Blockers NVARCHAR(500),
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );

-- 8. TABLA BURNDOWNDATA
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BurndownData')
    CREATE TABLE BurndownData (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        SprintId UNIQUEIDENTIFIER NOT NULL REFERENCES Sprints(Id),
        Date DATE NOT NULL,
        RemainingStoryPoints INT,
        RemainingHours INT,
        IdealRemaining DECIMAL(10,2)
    );

-- 9. TABLA STORYCOMMENTS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StoryComments')
    CREATE TABLE StoryComments (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        StoryId UNIQUEIDENTIFIER NOT NULL REFERENCES UserStories(Id),
        UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
        Message NVARCHAR(1000) NOT NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );

-- 10. TABLA STORYHISTORY
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StoryHistory')
    CREATE TABLE StoryHistory (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        StoryId UNIQUEIDENTIFIER NOT NULL REFERENCES UserStories(Id),
        UserId UNIQUEIDENTIFIER NULL REFERENCES Users(Id),
        EventType NVARCHAR(50) NOT NULL,
        Message NVARCHAR(1000) NOT NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );

-- 11. TABLA NOTIFICATIONS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Notifications')
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
    );

-- 12. TABLA PROJECTINVITATIONS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectInvitations')
    CREATE TABLE ProjectInvitations (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        ProjectId UNIQUEIDENTIFIER NOT NULL REFERENCES Projects(Id),
        UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
        InvitedById UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
        Role NVARCHAR(50) NOT NULL DEFAULT 'Developer',
        Status NVARCHAR(50) NOT NULL DEFAULT 'pending',
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        RespondedAt DATETIME2 NULL
    );

-- 13. TABLA SPRINTRETROSPECTIVES
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SprintRetrospectives')
    CREATE TABLE SprintRetrospectives (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        SprintId UNIQUEIDENTIFIER NOT NULL REFERENCES Sprints(Id),
        FacilitatorId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
        Date DATETIME2 NOT NULL DEFAULT GETDATE(),
        MoodRating DECIMAL(3,1) NOT NULL DEFAULT 5.0,
        Template NVARCHAR(50) DEFAULT 'StartStopContinue',
        Notes NVARCHAR(1000),
        IsCompleted BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NULL
    );

-- 14. TABLA RETROSPECTIVEITEMS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RetrospectiveItems')
    CREATE TABLE RetrospectiveItems (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        RetrospectiveId UNIQUEIDENTIFIER NOT NULL REFERENCES SprintRetrospectives(Id),
        Type NVARCHAR(20) NOT NULL CHECK (Type IN ('Start', 'Stop', 'Continue', 'Well', 'Improve', 'Puzzled', 'Liked')),
        Content NVARCHAR(1000) NOT NULL,
        UserId UNIQUEIDENTIFIER NULL REFERENCES Users(Id),
        IsAnonymous BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );

-- 15. TABLA RETROSPECTIVEACTIONITEMS
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RetrospectiveActionItems')
    CREATE TABLE RetrospectiveActionItems (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        RetrospectiveId UNIQUEIDENTIFIER NOT NULL REFERENCES SprintRetrospectives(Id),
        Description NVARCHAR(500) NOT NULL,
        AssigneeId UNIQUEIDENTIFIER REFERENCES Users(Id),
        DueDate DATE,
        Status NVARCHAR(20) DEFAULT 'Pending' CHECK (Status IN ('Pending', 'InProgress', 'Completed', 'Cancelled')),
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        UpdatedAt DATETIME2 DEFAULT GETDATE()
    );

-- ========================================
-- INSERCIÓN DE DATOS DE EJEMPLO
-- ========================================

-- Insertar usuarios de ejemplo
INSERT INTO Users (Id, Name, Email, PasswordHash, Role, CreatedAt) VALUES 
(NEWID(), 'Admin User', 'admin@scrum.com', '$2a$10$YourHashedPasswordHere', 'ProductOwner', GETDATE()),
(NEWID(), 'Scrum Master', 'scrum@scrum.com', '$2a$10$YourHashedPasswordHere', 'ScrumMaster', GETDATE()),
(NEWID(), 'Developer 1', 'dev1@scrum.com', '$2a$10$YourHashedPasswordHere', 'Developer', GETDATE()),
(NEWID(), 'Developer 2', 'dev2@scrum.com', '$2a$10$YourHashedPasswordHere', 'Developer', GETDATE());

-- ========================================
-- RELACIONES ENTRE TABLAS
-- ========================================

-- Projects -> Sprints (1:N)
-- Projects -> ProjectMembers (1:N)
-- Projects -> UserStories (1:N)
-- Sprints -> UserStories (1:N)
-- Sprints -> StandupNotes (1:N)
-- Sprints -> SprintRetrospectives (1:N)
-- Sprints -> BurndownData (1:N)
-- UserStories -> Tasks (1:N)
-- UserStories -> StoryComments (1:N)
-- UserStories -> StoryHistory (1:N)
-- SprintRetrospectives -> RetrospectiveItems (1:N)
-- SprintRetrospectives -> RetrospectiveActionItems (1:N)
-- Users -> ProjectMembers (1:N)
-- Users -> UserStories (1:N)
-- Users -> Tasks (1:N)
-- Users -> StandupNotes (1:N)
-- Users -> StoryComments (1:N)
-- Users -> StoryHistory (1:N)
-- Users -> Notifications (1:N)
-- Users -> ProjectInvitations (1:N)
-- Users -> SprintRetrospectives (1:N)
-- Users -> RetrospectiveActionItems (1:N)
