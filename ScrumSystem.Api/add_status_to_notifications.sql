-- Add Status column to Notifications table
ALTER TABLE Notifications ADD Status NVARCHAR(20) DEFAULT 'pending';
