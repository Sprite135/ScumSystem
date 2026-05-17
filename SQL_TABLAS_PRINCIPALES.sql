-- ========================================
-- TABLAS PRINCIPALES - SCRUM SYSTEM
-- ========================================

-- 1. TABLA PROJECTS
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

-- 2. TABLA SPRINTS
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

-- 3. TABLA USERSTORIES (STORIES)
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

-- 4. TABLA TASKS
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

-- 5. TABLA SUBTASKS (DERIVADA DE TASKS)
-- Nota: Las subtareas se manejan como Tasks adicionales
-- No hay tabla separada de SubTasks, se usa la misma tabla Tasks
-- con StoryId referenciando a la User Story principal
