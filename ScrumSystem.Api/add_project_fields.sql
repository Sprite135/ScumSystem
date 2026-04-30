-- Add Key, Color, and Icon columns to Projects table
ALTER TABLE Projects ADD Key NVARCHAR(10) NULL;
ALTER TABLE Projects ADD Color NVARCHAR(20) NULL;
ALTER TABLE Projects ADD Icon NVARCHAR(50) NULL;
