# Scrum System - Sistema de Gestión Ágil

Sistema web completo para gestión de proyectos Scrum con .NET 8, SQL Server y frontend vanilla HTML/CSS/JavaScript.

## Características

- **Gestión de Proyectos**: Crear y administrar proyectos con Product Owner y Scrum Master
- **Product Backlog**: Historias de usuario con prioridad y estimación
- **Sprints**: Planificación de sprints con fechas y metas
- **Sprint Backlog**: Asignación de historias a sprints
- **Tablero Kanban**: Visualización de tareas (Todo, In Progress, Done, Blocked)
- **Daily Standup**: Registro de reuniones diarias
- **Roles**: Product Owner, Scrum Master y Developers
- **Dashboard**: Estadísticas en tiempo real

## Tecnologías

- **Backend**: .NET 8 Web API (C#)
- **Base de Datos**: SQL Server (LocalDB o SQL Server Express)
- **Frontend**: HTML5, CSS3, JavaScript Vanilla
- **Autenticación**: BCrypt para hashing de contraseñas
- **Estilos**: CSS moderno con variables y flexbox/grid

## Estructura del Proyecto

```
Proyecto_Tesis/
├── ScrumSystem.sln                    # Solución de Visual Studio
├── ScrumSystem.Api/                   # Proyecto Web API
│   ├── wwwroot/                       # Frontend estático
│   │   ├── index.html
│   │   ├── css/styles.css
│   │   └── js/app.js
│   ├── Models/                        # Modelos y base de datos
│   │   ├── DatabaseContext.cs
│   │   └── Entities.cs
│   ├── Routes/                        # Endpoints de la API
│   │   ├── UserRoutes.cs
│   │   ├── ProjectRoutes.cs
│   │   ├── SprintRoutes.cs
│   │   ├── StoryRoutes.cs
│   │   ├── TaskRoutes.cs
│   │   ├── StandupRoutes.cs
│   │   └── DashboardRoutes.cs
│   ├── Program.cs
│   ├── appsettings.json
│   └── ScrumSystem.Api.csproj
└── README.md
```

## Requisitos Previos

1. **.NET 8 SDK** - [Descargar](https://dotnet.microsoft.com/download/dotnet/8.0)
2. **SQL Server** (LocalDB, Express o completo)
3. **Visual Studio 2022** (opcional) o VS Code

## Configuración de Base de Datos

La cadena de conexión por defecto usa SQL Server LocalDB:

```json
"ConnectionStrings": {
    "DefaultConnection": "Server=(local)\\SQLEXPRESS;Database=ScrumSystem;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

Para usar otra instancia de SQL Server, modifica `appsettings.json`.

## Instalación y Ejecución

### Opción 1: Línea de Comandos

```bash
# 1. Navegar al proyecto
cd ScrumSystem.Api

# 2. Restaurar paquetes
dotnet restore

# 3. Ejecutar la aplicación
dotnet run

# 4. Abrir navegador en:
# https://localhost:5001 o http://localhost:5000
```

### Opción 2: Visual Studio

1. Abrir `ScrumSystem.sln` en Visual Studio 2022
2. Presionar `F5` o clic en **Start**
3. El navegador se abrirá automáticamente

## Credenciales de Demo

El sistema crea automáticamente estos usuarios al iniciar:

| Usuario | Email | Contraseña | Rol |
|---------|-------|------------|-----|
| Admin | admin@scrum.com | admin123 | Product Owner |
| Scrum Master | scrum@scrum.com | admin123 | Scrum Master |
| Developer 1 | dev1@scrum.com | admin123 | Developer |
| Developer 2 | dev2@scrum.com | admin123 | Developer |

## API Endpoints

### Usuarios
- `POST /api/users` - Crear usuario
- `POST /api/users/login` - Login
- `GET /api/users` - Listar usuarios

### Proyectos
- `POST /api/projects` - Crear proyecto
- `GET /api/projects` - Listar proyectos
- `GET /api/projects/{id}` - Obtener proyecto

### Sprints
- `POST /api/sprints` - Crear sprint
- `GET /api/sprints/project/{projectId}` - Sprints por proyecto
- `GET /api/sprints/{id}/burndown` - Datos del burndown chart

### Historias
- `POST /api/stories` - Crear historia
- `GET /api/stories/project/{projectId}/backlog` - Backlog
- `GET /api/stories/sprint/{sprintId}` - Historias del sprint

### Tareas
- `POST /api/tasks` - Crear tarea
- `GET /api/tasks/board/{sprintId}` - Tablero Kanban
- `PATCH /api/tasks/{id}/status` - Actualizar estado

### Daily Standup
- `POST /api/standup` - Registrar nota
- `GET /api/standup/sprint/{sprintId}/today` - Notas de hoy

## Funcionalidades Scrum Implementadas

| Funcionalidad | Descripción |
|---------------|-------------|
| **Product Backlog** | Lista priorizada de historias de usuario |
| **Sprint Planning** | Asignar historias a sprints |
| **Sprint Backlog** | Historias seleccionadas para el sprint |
| **Kanban Board** | Visualización de flujo de trabajo |
| **Daily Standup** | Registro de reuniones diarias |
| **Roles** | PO, SM y Developers con permisos |
| **Estimación** | Story points en historias |
| **Priorización** | Alta, Media, Baja, Crítica |

## Screenshots del Flujo

1. **Login**: Pantalla de inicio de sesión con credenciales demo
2. **Dashboard**: Estadísticas generales del sistema
3. **Proyectos**: Lista de proyectos con tarjetas informativas
4. **Backlog**: Historias de usuario con puntos y prioridad
5. **Sprints**: Gestión de sprints con progreso visual
6. **Tablero**: Vista Kanban con drag-and-drop visual
7. **Standup**: Formulario y listado de registros diarios

## Desarrollo

### Agregar Nuevas Características

1. Crear modelo en `Models/Entities.cs`
2. Agregar ruta en `Routes/` correspondiente
3. Registrar ruta en `Program.cs`
4. Actualizar frontend en `wwwroot/`

### Compilar para Producción

```bash
dotnet publish -c Release -o ./publish
```

## Licencia

Proyecto académico para gestión de proyectos Scrum.
