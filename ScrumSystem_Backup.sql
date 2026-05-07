-- =====================================================
-- SCRUM SYSTEM DATABASE BACKUP
-- Proyecto de Tesis
-- Fecha de generación: 2026-05-07
-- =====================================================
-- Este script contiene la estructura completa de la base de datos
-- para fines de documentación y recreación del esquema.
-- =====================================================
-- NOTA: Para respaldo completo de datos, usar:
-- BACKUP DATABASE ScrumSystem TO DISK = 'ScrumSystem_Backup.bak'
-- =====================================================

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'ScrumSystem')
BEGIN
    CREATE DATABASE ScrumSystem;
END
GO

USE ScrumSystem;
GO

-- =====================================================
-- CREACIÓN DE TABLAS
-- =====================================================

-- Tabla Users
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
CREATE TABLE Users (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Name NVARCHAR(100) NOT NULL,
    Email NVARCHAR(100) UNIQUE NOT NULL,
    PasswordHash NVARCHAR(200) NOT NULL,
    Role NVARCHAR(20) NOT NULL CHECK (Role IN ('ProductOwner', 'ScrumMaster', 'Developer')),
    CreatedAt DATETIME2 DEFAULT GETDATE()
);

-- Tabla Projects
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Projects')
CREATE TABLE Projects (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500),
    [Key] NVARCHAR(10),
    Color NVARCHAR(7),
    Icon NVARCHAR(50),
    CreatorId UNIQUEIDENTIFIER REFERENCES Users(Id),
    ProductOwnerId UNIQUEIDENTIFIER REFERENCES Users(Id),
    ScrumMasterId UNIQUEIDENTIFIER REFERENCES Users(Id),
    CreatedAt DATETIME2 DEFAULT GETDATE()
);

-- Tabla ProjectMembers
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectMembers')
CREATE TABLE ProjectMembers (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ProjectId UNIQUEIDENTIFIER REFERENCES Projects(Id),
    UserId UNIQUEIDENTIFIER REFERENCES Users(Id),
    Role NVARCHAR(50) DEFAULT 'Developer',
    JoinedAt DATETIME2 DEFAULT GETDATE()
);

-- Tabla Sprints
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
    UpdatedAt DATETIME2 DEFAULT GETDATE(),
    CreatedAt DATETIME2 DEFAULT GETDATE()
);

-- Tabla UserStories
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserStories')
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
);

-- Tabla Tasks
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

-- Tabla StandupNotes
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

-- Tabla BurndownData
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BurndownData')
CREATE TABLE BurndownData (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    SprintId UNIQUEIDENTIFIER NOT NULL REFERENCES Sprints(Id),
    Date DATE NOT NULL,
    RemainingStoryPoints INT,
    RemainingHours INT,
    IdealRemaining DECIMAL(10,2)
);

-- Tabla StoryComments
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StoryComments')
CREATE TABLE StoryComments (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    StoryId UNIQUEIDENTIFIER NOT NULL REFERENCES UserStories(Id),
    UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
    Message NVARCHAR(1000) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE()
);

-- Tabla StoryHistory
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'StoryHistory')
CREATE TABLE StoryHistory (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    StoryId UNIQUEIDENTIFIER NOT NULL REFERENCES UserStories(Id),
    UserId UNIQUEIDENTIFIER NULL REFERENCES Users(Id),
    EventType NVARCHAR(50) NOT NULL,
    Message NVARCHAR(1000) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE()
);

-- Tabla Notifications
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

-- Tabla ProjectInvitations
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

GO

-- =====================================================
-- COMANDO PARA RESPALDO COMPLETO DE DATOS
-- =====================================================
-- Ejecutar este comando en SQL Server Management Studio
-- para crear un respaldo completo con todos los datos:
-- 
-- BACKUP DATABASE ScrumSystem 
-- TO DISK = 'D:\Proyecto_Tesis\ScrumSystem_FullBackup.bak'
-- WITH FORMAT,
-- MEDIANAME = 'ScrumSystem_Full',
-- NAME = 'Full Backup of ScrumSystem';
-- 
-- Para restaurar:
-- RESTORE DATABASE ScrumSystem 
-- FROM DISK = 'D:\Proyecto_Tesis\ScrumSystem_FullBackup.bak'
-- WITH REPLACE;
-- =====================================================

PRINT 'Estructura de base de datos creada exitosamente.';
PRINT 'Para respaldo completo de datos, usar BACKUP DATABASE.';
GO
