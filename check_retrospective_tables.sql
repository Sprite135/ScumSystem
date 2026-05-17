-- Verificar si las tablas de retrospectives existen
SELECT 
    TABLE_NAME,
    TABLE_TYPE,
    CREATE_DATE
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_NAME LIKE '%Retrospective%'
ORDER BY TABLE_NAME;

-- Verificar estructura de la tabla SprintRetrospectives
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    IS_NULLABLE,
    COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'SprintRetrospectives'
ORDER BY ORDINAL_POSITION;

-- Verificar estructura de la tabla RetrospectiveItems
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    IS_NULLABLE,
    COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'RetrospectiveItems'
ORDER BY ORDINAL_POSITION;

-- Verificar estructura de la tabla RetrospectiveActionItems
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    IS_NULLABLE,
    COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'RetrospectiveActionItems'
ORDER BY ORDINAL_POSITION;

-- Contar registros en cada tabla
SELECT 
    'SprintRetrospectives' as TableName,
    COUNT(*) as RecordCount
FROM SprintRetrospectives
UNION ALL
SELECT 
    'RetrospectiveItems' as TableName,
    COUNT(*) as RecordCount
FROM RetrospectiveItems
UNION ALL
SELECT 
    'RetrospectiveActionItems' as TableName,
    COUNT(*) as RecordCount
FROM RetrospectiveActionItems;
