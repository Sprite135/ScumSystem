-- Agregar columnas faltantes a la tabla Projects

-- Primero verificar si las columnas existen
IF NOT EXISTS (SELECT * FROM sys.columns WHERE name = '[Key]' AND object_id = OBJECT_ID('Projects'))
    ALTER TABLE Projects ADD [Key] NVARCHAR(20);

IF NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'Color' AND object_id = OBJECT_ID('Projects'))
    ALTER TABLE Projects ADD Color NVARCHAR(20);

IF NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'Icon' AND object_id = OBJECT_ID('Projects'))
    ALTER TABLE Projects ADD Icon NVARCHAR(50);

PRINT 'Columnas agregadas correctamente';
