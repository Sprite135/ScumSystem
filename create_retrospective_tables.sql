-- =====================================================
-- CREATE RETROSPECTIVE TABLES
-- Sprint Retrospective Module
-- =====================================================

USE ScrumSystem;
GO

-- Create SprintRetrospectives table
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
GO

-- Create RetrospectiveItems table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RetrospectiveItems')
CREATE TABLE RetrospectiveItems (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    RetrospectiveId UNIQUEIDENTIFIER NOT NULL REFERENCES SprintRetrospectives(Id),
    Type NVARCHAR(50) NOT NULL, -- "Well", "Improve", "Start", "Stop", "Continue"
    Content NVARCHAR(500) NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
    Votes INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 DEFAULT GETDATE()
);
GO

-- Create RetrospectiveItemVotes table (for tracking who voted)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RetrospectiveItemVotes')
CREATE TABLE RetrospectiveItemVotes (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ItemId UNIQUEIDENTIFIER NOT NULL REFERENCES RetrospectiveItems(Id),
    UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    CONSTRAINT UQ_RetrospectiveItemVotes UNIQUE (ItemId, UserId)
);
GO

-- Create RetrospectiveActionItems table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RetrospectiveActionItems')
CREATE TABLE RetrospectiveActionItems (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    RetrospectiveId UNIQUEIDENTIFIER NOT NULL REFERENCES SprintRetrospectives(Id),
    Action NVARCHAR(500) NOT NULL,
    AssignedToId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
    DueDate DATE NOT NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending' CHECK (Status IN ('Pending', 'InProgress', 'Completed')),
    CompletedAt DATETIME2 NULL,
    CreatedById UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
    CreatedAt DATETIME2 DEFAULT GETDATE()
);
GO

-- Create indexes for performance
CREATE INDEX IX_SprintRetrospectives_SprintId ON SprintRetrospectives(SprintId);
CREATE INDEX IX_SprintRetrospectives_FacilitatorId ON SprintRetrospectives(FacilitatorId);
CREATE INDEX IX_RetrospectiveItems_RetrospectiveId ON RetrospectiveItems(RetrospectiveId);
CREATE INDEX IX_RetrospectiveItems_UserId ON RetrospectiveItems(UserId);
CREATE INDEX IX_RetrospectiveActionItems_RetrospectiveId ON RetrospectiveActionItems(RetrospectiveId);
CREATE INDEX IX_RetrospectiveActionItems_AssignedToId ON RetrospectiveActionItems(AssignedToId);
GO

PRINT 'Retrospective tables created successfully!';
GO
