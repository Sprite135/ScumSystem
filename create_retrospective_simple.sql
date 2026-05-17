-- Script simple para crear tablas de Retrospective
-- Copia y pega esto en SQL Server Management Studio

USE ScrumSystem;
GO

-- Tabla principal de Retrospectives
CREATE TABLE SprintRetrospectives (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    SprintId UNIQUEIDENTIFIER NOT NULL,
    FacilitatorId UNIQUEIDENTIFIER NOT NULL,
    Date DATETIME2 DEFAULT GETDATE(),
    MoodRating DECIMAL(3,1) DEFAULT 5.0,
    Template NVARCHAR(50) DEFAULT 'StartStopContinue',
    Notes NVARCHAR(1000),
    IsCompleted BIT DEFAULT 0,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    UpdatedAt DATETIME2 NULL
);
GO

-- Tabla de ideas/items de retrospective
CREATE TABLE RetrospectiveItems (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    RetrospectiveId UNIQUEIDENTIFIER NOT NULL,
    Type NVARCHAR(50) NOT NULL,
    Content NVARCHAR(500) NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    Votes INT DEFAULT 0,
    CreatedAt DATETIME2 DEFAULT GETDATE()
);
GO

-- Tabla de acciones de mejora
CREATE TABLE RetrospectiveActionItems (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    RetrospectiveId UNIQUEIDENTIFIER NOT NULL,
    Action NVARCHAR(500) NOT NULL,
    AssignedToId UNIQUEIDENTIFIER NOT NULL,
    DueDate DATE NOT NULL,
    Status NVARCHAR(20) DEFAULT 'Pending',
    CompletedAt DATETIME2 NULL,
    CreatedById UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE()
);
GO

PRINT 'Tablas de Retrospective creadas exitosamente';
GO
