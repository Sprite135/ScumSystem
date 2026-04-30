-- Script para limpiar todos los proyectos y datos relacionados
-- Ejecutar con precaución - borra TODO

-- Desactivar foreign keys temporalmente (si es necesario)
-- Borrar en orden correcto por dependencias

DELETE FROM StandupNotes;
DELETE FROM Tasks;
DELETE FROM UserStories;
DELETE FROM Sprints;
DELETE FROM ProjectMembers;
DELETE FROM Notifications;
DELETE FROM Projects;

PRINT 'Todos los proyectos han sido eliminados';
