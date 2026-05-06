// API Configuration
const API_URL = '';

// Global State
let currentUser = null;
let projects = [];
let sprints = [];
let stories = [];
let burndownChart = null;
let membersToAdd = [];
let nextSprintNumber = 2; // Sprint 1 already exists, next will be Sprint 2
let storyToDelete = null; // Stores story ID pending deletion
let taskCache = new Map(); // Global task cache
let preventIssueDetailReload = false; // Prevent reload after assignment changes

// Initialize app
document.addEventListener('DOMContentLoaded', () => {
    checkAuth();
    setupEventListeners();
    
    // Handle hash changes (back button, refresh)
    window.addEventListener('hashchange', () => {
        const hash = window.location.hash.substring(1);
        if (hash) {
            const pageName = hash.split('/')[0];
            loadPage(pageName);
        }
    });
});

// ==================== AUTHENTICATION ====================
function checkAuth() {
    const user = localStorage.getItem('scrumUser');
    if (user) {
        currentUser = JSON.parse(user);
        showMainApp();
        
        // Load page based on hash or default to welcome
        const hash = window.location.hash.substring(1);
        const pageName = hash ? hash.split('/')[0] : 'welcome';
        console.log('Loading page:', pageName, 'from hash:', hash);
        loadPage(pageName);
    } else {
        window.location.href = 'login.html';
    }
}

function showMainApp() {
    const mainApp = document.getElementById('main-app');
    if (mainApp) mainApp.style.display = 'flex';
    
    // Update user info in sidebar
    const userName = document.getElementById('user-name');
    const userRole = document.getElementById('user-role');
    const userAvatar = document.getElementById('user-avatar');
    
    if (currentUser) {
        if (userName) userName.textContent = currentUser.name;
        if (userRole) {
            const roleMap = { 0: 'Product Owner', 1: 'Scrum Master', 2: 'Developer' };
            const roleText = typeof currentUser.role === 'number' ? roleMap[currentUser.role] : currentUser.role;
            userRole.textContent = roleText || 'Usuario';
        }
        if (userAvatar) {
            const initials = currentUser.name?.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2) || 'U';
            userAvatar.textContent = initials;
        }
        
        // Load notifications
        loadNotifications();
    }
}

function logout() {
    localStorage.removeItem('scrumUser');
    // Clear cache to ensure fresh data on next login
    taskCache.clear();
    window.location.href = 'login.html';
}

// ==================== NAVIGATION ====================
async function loadPage(pageName) {
    const mainContent = document.getElementById('main-content');
    if (!mainContent) return;
    
    // Handle project page specially (rendered in JS)
    if (pageName === 'project') {
        initPage('project');
        return;
    }
    
    try {
        const response = await fetch(`pages/${pageName}.html`);
        if (!response.ok) throw new Error(`Failed to load: ${response.status}`);
        
        const html = await response.text();
        mainContent.innerHTML = html;
        
        const pageElement = mainContent.querySelector('.page');
        if (pageElement) pageElement.classList.add('active');
        
        initPage(pageName);
    } catch (error) {
        console.error('Error loading page:', error);
        mainContent.innerHTML = `<div class="page active"><h2>Error</h2><p>${error.message}</p></div>`;
    }
}

function initPage(pageName) {
    switch(pageName) {
        case 'welcome': loadWelcome(); break;
        case 'dashboard': loadDashboard(); setupDashboardListener(); break;
        case 'projects': loadProjects(); break;
        case 'backlog': loadBacklog(); setupBacklogListener(); break;
        case 'board': loadBoard(); setupBoardListener(); break;
        case 'project': loadProjectView(); break;
    }
}

function navigateTo(page) {
    document.querySelectorAll('.nav-item').forEach(item => {
        item.classList.remove('active');
        if (item.dataset.page === page) item.classList.add('active');
    });
    window.location.hash = page;
    loadPage(page);
}

function setupEventListeners() {
    document.getElementById('logout-btn')?.addEventListener('click', logout);
    document.querySelectorAll('.nav-item').forEach(item => {
        item.addEventListener('click', (e) => {
            e.preventDefault();
            navigateTo(item.dataset.page);
        });
    });
    document.getElementById('project-form')?.addEventListener('submit', handleCreateProject);
    document.getElementById('story-form')?.addEventListener('submit', handleCreateStory);
}

// ==================== API ====================
async function apiRequest(endpoint, options = {}) {
    const url = `${API_URL}${endpoint}`;
    const config = {
        headers: {
            'Content-Type': 'application/json',
            ...options.headers
        },
        ...options
    };
    
    if (config.body && typeof config.body === 'object') {
        config.body = JSON.stringify(config.body);
    }
    
    const response = await fetch(url, config);
    
    if (!response.ok) {
        const error = await response.text();
        throw new Error(error || `HTTP ${response.status}`);
    }
    
    if (response.status === 204) return null;
    return response.json();
}

// ==================== WELCOME ====================
async function loadWelcome() {
    console.log('=== loadWelcome START ===');
    
    // Update welcome message with user name
    const welcomeMessage = document.getElementById('welcome-message');
    if (welcomeMessage && currentUser) {
        const hour = new Date().getHours();
        let greeting = 'Buenos días';
        if (hour >= 12 && hour < 18) greeting = 'Buenas tardes';
        else if (hour >= 18) greeting = 'Buenas noches';
        
        welcomeMessage.textContent = `${greeting}, ${currentUser.name}! Estás listo para gestionar tus proyectos Scrum`;
    }
    
    try {
        // Load projects
        await loadProjects();
        
        // Load stats
        const stats = await apiRequest('/api/dashboard/stats');
        document.getElementById('welcome-projects').textContent = stats.totalProjects || 0;
        document.getElementById('welcome-stories').textContent = stats.totalStories || 0;
        document.getElementById('welcome-tasks').textContent = stats.totalTasks || 0;
        
        // Load recent projects
        loadRecentProjects();
    } catch (error) {
        console.error('Error loading welcome page:', error);
    }
}

function loadRecentProjects() {
    const recentProjectsGrid = document.getElementById('recent-projects-grid');
    const recentProjectsSection = document.getElementById('recent-projects');
    
    if (!recentProjectsGrid || !recentProjectsSection) return;
    
    if (!projects || projects.length === 0) {
        recentProjectsSection.style.display = 'none';
        return;
    }
    
    // Show only first 6 projects, most recent first
    const recentProjects = projects
        .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))
        .slice(0, 6);
    
    recentProjectsGrid.innerHTML = recentProjects.map(project => {
        return `
            <div class="project-card-welcome" onclick="selectProject('${project.id}')">
                <h4>${escapeHtml(project.name)}</h4>
                <p>${new Date(project.createdAt).toLocaleDateString()}</p>
            </div>
        `;
    }).join('');
}

function navigateToFirstProject() {
    if (projects && projects.length > 0) {
        selectProject(projects[0].id);
    } else {
        showModal('project-modal');
    }
}

function showCreateQuickStory() {
    if (!projects || projects.length === 0) {
        showToast('Primero crea un proyecto', 'warning');
        showModal('project-modal');
        return;
    }
    
    // Navigate to first project and show story modal
    selectProject(projects[0].id);
    setTimeout(() => {
        showModal('story-modal');
    }, 500);
}

// ==================== DASHBOARD ====================
async function loadDashboard() {
    if (!projects.length) await loadProjects();
    
    // Populate project selector
    const projectSelect = document.getElementById('dashboard-project-select');
    if (projectSelect && projects.length) {
        const current = projectSelect.value;
        projectSelect.innerHTML = '<option value="">Seleccionar Proyecto</option>' + 
            projects.map(p => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('');
        if (current && projects.find(p => p.id === current)) {
            projectSelect.value = current;
        } else if (projects.length) {
            projectSelect.value = projects[0].id;
        }
    }
    
    const projectId = projectSelect?.value || projects[0]?.id;
    if (!projectId) return;
    
    try {
        // Load stats
        const stats = await apiRequest('/api/dashboard/stats');
        const statProjects = document.getElementById('stat-projects');
        const statStories = document.getElementById('stat-stories');
        const statTasks = document.getElementById('stat-tasks');
        if (statProjects) statProjects.textContent = stats.totalProjects || 0;
        if (statStories) statStories.textContent = stats.totalStories || 0;
        if (statTasks) statTasks.textContent = stats.completedTasks || 0;
        
        // Load stories for dashboard
        const stories = await apiRequest(`/api/stories/project/${projectId}/backlog`);
        renderDashboardStories(stories);
        
        // Calculate story points and completed
        const totalPoints = stories.reduce((sum, s) => sum + (s.storyPoints || 0), 0);
        const completed = stories.filter(s => s.status === 'Done').length;
        const statPoints = document.getElementById('stat-points');
        const statDone = document.getElementById('stat-done');
        if (statPoints) statPoints.textContent = totalPoints;
        if (statDone) statDone.textContent = completed;
        
        loadBurndownSprints();
    } catch (error) {
        console.error('Error loading dashboard:', error);
    }
}

function renderDashboardStories(stories) {
    const container = document.getElementById('dashboard-backlog-list');
    if (!container) return;
    
    if (!stories.length) {
        container.innerHTML = '<div class="empty-state"><i class="fas fa-list"></i><p>No hay historias en el backlog</p></div>';
        return;
    }
    
    container.innerHTML = stories.map(s => `
        <div class="backlog-item priority-${s.priority}">
            <div class="backlog-item-content">
                <div class="backlog-item-header">
                    <span class="story-id">#${s.id.substring(0, 8)}</span>
                    <span class="badge priority">${getPriorityText(s.priority)}</span>
                    <div class="story-actions">
                        <button class="btn btn-icon btn-small" onclick="editStory('${s.id}')" title="Editar">
                            <i class="fas fa-edit"></i>
                        </button>
                        <button class="btn btn-icon btn-small text-danger" onclick="deleteStory('${s.id}')" title="Eliminar">
                            <i class="fas fa-trash"></i>
                        </button>
                    </div>
                </div>
                <h4>${escapeHtml(s.title)}</h4>
                <p>${escapeHtml(s.description || 'Sin descripción')}</p>
                <div class="backlog-item-footer">
                    <div class="story-points">
                        <i class="fas fa-star"></i> ${s.storyPoints || 0} pts
                    </div>
                    <div class="task-count">
                        <i class="fas fa-tasks"></i> ${s.taskCount || 0} tareas
                    </div>
                </div>
            </div>
        </div>
    `).join('');
}

function setupDashboardListener() {
    const select = document.getElementById('dashboard-project-select');
    if (select) select.addEventListener('change', loadDashboard);
}

async function loadBurndownSprints() {
    try {
        const sprints = await apiRequest('/api/sprints');
        const select = document.getElementById('burndown-sprint-select');
        if (select) {
            select.innerHTML = '<option value="">Seleccionar Sprint</option>' + 
                sprints.map(s => `<option value="${s.id}">${s.name}</option>`).join('');
            select.onchange = (e) => { if (e.target.value) loadBurndownChart(e.target.value); };
        }
    } catch (error) {
        console.error('Error loading sprints for burndown:', error);
    }
}

async function loadBurndownChart(sprintId) {
    try {
        const data = await apiRequest(`/api/sprints/${sprintId}/burndown`);
        const ctx = document.getElementById('burndown-chart')?.getContext('2d');
        if (!ctx) return;
        
        if (burndownChart) burndownChart.destroy();
        
        burndownChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: data.labels,
                datasets: [
                    {
                        label: 'Ideal',
                        data: data.ideal,
                        borderColor: 'rgba(139, 92, 246, 0.5)',
                        borderDash: [5, 5],
                        fill: false,
                        tension: 0
                    },
                    {
                        label: 'Actual',
                        data: data.actual,
                        borderColor: 'rgba(139, 92, 246, 1)',
                        backgroundColor: 'rgba(139, 92, 246, 0.1)',
                        fill: true,
                        tension: 0.3
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { position: 'top', labels: { color: '#ffffff' } }
                },
                scales: {
                    x: { grid: { color: 'rgba(255, 255, 255, 0.1)' }, ticks: { color: '#ffffff' } },
                    y: { grid: { color: 'rgba(255, 255, 255, 0.1)' }, ticks: { color: '#ffffff' }, beginAtZero: true }
                }
            }
        });
    } catch (error) {
        console.error('Error loading burndown:', error);
    }
}

// ==================== PROJECTS ====================
let selectedProjectId = null;

async function loadProjects() {
    try {
        projects = await apiRequest(`/api/projects?userId=${currentUser.id}`);
        renderSidebarProjects();
        populateProjectSelects();
    } catch (error) {
        console.error('Error loading projects:', error);
    }
}

function renderSidebarProjects() {
    const container = document.getElementById('sidebar-projects-list');
    if (!container) return;
    
    if (projects.length === 0) {
        container.innerHTML = '<div class="text-muted text-center" style="padding: 20px; font-size: 12px;">Sin proyectos</div>';
        return;
    }
    
    const iconMap = {
        'folder': '<i class="fas fa-folder"></i>',
        'rocket': '<i class="fas fa-rocket"></i>',
        'code': '<i class="fas fa-code"></i>',
        'mobile': '<i class="fas fa-mobile-alt"></i>',
        'globe': '<i class="fas fa-globe"></i>'
    };
    
    // Separate projects by ownership
    const currentUserId = currentUser?.id?.toString().toLowerCase();
    const myProjects = projects.filter(p => {
        const projectOwnerId = p.creatorId?.toString().toLowerCase();
        return currentUserId && projectOwnerId && currentUserId === projectOwnerId;
    });
    
    const otherProjects = projects.filter(p => {
        const projectOwnerId = p.creatorId?.toString().toLowerCase();
        return !currentUserId || !projectOwnerId || currentUserId !== projectOwnerId;
    });
    
    let html = '';
    
    // My Projects (Created by me)
    if (myProjects.length > 0) {
        html += `
            <div class="projects-section">
                <div class="projects-section-header">
                    <i class="fas fa-crown"></i>
                    <span>Mis Proyectos</span>
                    <small style="color: var(--text-muted); font-size: 11px;">Creados por mí</small>
                </div>
                <div class="projects-list">
                    ${myProjects.map(p => {
                        const iconHtml = iconMap[p.icon] || iconMap['folder'];
                        const iconStyle = p.color ? `background: ${p.color};` : '';
                        const isActive = selectedProjectId === p.id ? 'active' : '';
                        const isCreator = true;
                        
                        return `
                            <div class="sidebar-project-item ${isActive}" onclick="selectProject('${p.id}')">
                                <div class="sidebar-project-icon" style="${iconStyle}">
                                    ${iconHtml}
                                </div>
                                <span class="sidebar-project-name">${escapeHtml(p.name)}</span>
                                <button class="sidebar-project-menu-btn" onclick="event.stopPropagation(); toggleProjectMenu('${p.id}', '${escapeHtml(p.name)}', ${isCreator})">
                                    <i class="fas fa-ellipsis-v"></i>
                                </button>
                                <div class="project-menu" id="project-menu-${p.id}" style="display: none;">
                                    <div class="project-menu-item" onclick="event.stopPropagation(); openAddMembersModal('${p.id}', '${escapeHtml(p.name)}')">
                                        <i class="fas fa-user-plus"></i> Agregar miembros
                                    </div>
                                    <div class="project-menu-item" onclick="event.stopPropagation(); openConfigureProjectModal('${p.id}', '${escapeHtml(p.name)}', '${escapeHtml(p.key || '')}', '${escapeHtml(p.color || '')}', '${escapeHtml(p.icon || '')}')">
                                        <i class="fas fa-cog"></i> Configurar
                                    </div>
                                    <div class="project-menu-item text-danger" onclick="event.stopPropagation(); deleteProject('${p.id}', '${escapeHtml(p.name)}')">
                                        <i class="fas fa-trash"></i> Eliminar proyecto
                                    </div>
                                </div>
                            </div>
                        `;
                    }).join('')}
                </div>
            </div>
        `;
    }
    
    // Other Projects (Joined by invitation)
    if (otherProjects.length > 0) {
        html += `
            <div class="projects-section">
                <div class="projects-section-header">
                    <i class="fas fa-users"></i>
                    <span>Otros Proyectos</span>
                    <small style="color: var(--text-muted); font-size: 11px;">Unido por invitación</small>
                </div>
                <div class="projects-list">
                    ${otherProjects.map(p => {
                        const iconHtml = iconMap[p.icon] || iconMap['folder'];
                        const iconStyle = p.color ? `background: ${p.color};` : '';
                        const isActive = selectedProjectId === p.id ? 'active' : '';
                        const isCreator = false;
                        
                        return `
                            <div class="sidebar-project-item ${isActive}" onclick="selectProject('${p.id}')">
                                <div class="sidebar-project-icon" style="${iconStyle}">
                                    ${iconHtml}
                                </div>
                                <span class="sidebar-project-name">${escapeHtml(p.name)}</span>
                                <button class="sidebar-project-menu-btn" onclick="event.stopPropagation(); toggleProjectMenu('${p.id}', '${escapeHtml(p.name)}', ${isCreator})">
                                    <i class="fas fa-ellipsis-v"></i>
                                </button>
                                <div class="project-menu" id="project-menu-${p.id}" style="display: none;">
                                    <div class="project-menu-item text-danger" onclick="event.stopPropagation(); leaveProject('${p.id}', '${escapeHtml(p.name)}')">
                                        <i class="fas fa-sign-out-alt"></i> Salir del proyecto
                                    </div>
                                </div>
                            </div>
                        `;
                    }).join('')}
                </div>
            </div>
        `;
    }
    
    container.innerHTML = html;
}

function toggleProjectMenu(projectId, projectName, isCreator) {
    const menu = document.getElementById(`project-menu-${projectId}`);
    if (!menu) return;
    
    // Close all other menus
    document.querySelectorAll('.project-menu').forEach(m => {
        if (m.id !== `project-menu-${projectId}`) {
            m.style.display = 'none';
        }
    });
    
    // Toggle current menu
    menu.style.display = menu.style.display === 'none' ? 'block' : 'none';
}

function selectProject(projectId) {
    selectedProjectId = projectId;
    renderSidebarProjects();
    // Navigate to project view with backlog as default sub-tab
    window.location.hash = `project/${projectId}/backlog`;
    loadPage('project');
}

let projectSubTab = 'backlog';

async function loadProjectView() {
    const mainContent = document.getElementById('main-content');
    if (!mainContent) return;
    
    const hash = window.location.hash;
    const parts = hash.split('/');
    const projectId = parts[1];
    projectSubTab = parts[2] || 'backlog';
    
    // Ensure projects are loaded
    if (!projects || projects.length === 0) {
        await loadProjects();
    }
    
    // Set selected project ID
    selectedProjectId = projectId;
    
    const project = projects.find(p => p.id === projectId);
    if (!project) {
        mainContent.innerHTML = '<div class="page active"><h2>Proyecto no encontrado</h2></div>';
        return;
    }
    
    const iconMap = {
        'folder': '<i class="fas fa-folder"></i>',
        'rocket': '<i class="fas fa-rocket"></i>',
        'code': '<i class="fas fa-code"></i>',
        'mobile': '<i class="fas fa-mobile-alt"></i>',
        'globe': '<i class="fas fa-globe"></i>'
    };
    const iconHtml = iconMap[project.icon] || iconMap['folder'];
    const iconStyle = project.color ? `background: ${project.color};` : '';
    
    mainContent.innerHTML = `
        <div class="page active">
            <div class="project-view-header">
                <div class="project-space-header">
                    <div class="project-space-icon" style="${iconStyle}">
                        ${iconHtml}
                    </div>
                    <div class="project-space-info">
                        <div class="project-space-label">Espacio</div>
                        <h1 class="project-space-name">${escapeHtml(project.name)}</h1>
                    </div>
                </div>
            </div>
            
            <div class="project-nav-tabs">
                <button class="project-nav-tab ${projectSubTab === 'dashboard' ? 'active' : ''}" onclick="switchProjectTab('dashboard')">
                    Dashboard
                </button>
                <button class="project-nav-tab ${projectSubTab === 'backlog' ? 'active' : ''}" onclick="switchProjectTab('backlog')">
                    Backlog
                </button>
                <button class="project-nav-tab ${projectSubTab === 'board' ? 'active' : ''}" onclick="switchProjectTab('board')">
                    Tablero
                </button>
            </div>
            
            <div class="project-controls">
                <div class="project-search">
                    <div class="search-input-wrapper">
                        <i class="fas fa-search"></i>
                        <input type="text" id="project-search-input" placeholder="Buscar tablero..." onkeyup="searchProjectStories(event)">
                    </div>
                </div>
                <div class="project-members" id="project-members-list">
                    <!-- Member avatars loaded here -->
                </div>
                <div class="project-filter">
                    <button class="btn btn-icon btn-secondary" onclick="toggleFilterMenu()" title="Filtros">
                        <i class="fas fa-filter"></i>
                    </button>
                    <div class="filter-menu" id="filter-menu" style="display: none;">
                        <div class="filter-menu-item" onclick="applyFilter('main')">
                            <i class="fas fa-home"></i> Principal
                        </div>
                        <div class="filter-menu-item" onclick="applyFilter('sprint')">
                            <i class="fas fa-running"></i> Sprint
                        </div>
                        <div class="filter-menu-item" onclick="applyFilter('assignee')">
                            <i class="fas fa-user"></i> Persona asignada
                        </div>
                        <div class="filter-menu-item" onclick="applyFilter('activity')">
                            <i class="fas fa-tasks"></i> Tipo de actividad
                        </div>
                        <div class="filter-menu-item" onclick="applyFilter('labels')">
                            <i class="fas fa-tag"></i> Etiquetas
                        </div>
                        <div class="filter-menu-item" onclick="applyFilter('status')">
                            <i class="fas fa-flag"></i> Estado
                        </div>
                        <div class="filter-menu-item" onclick="applyFilter('priority')">
                            <i class="fas fa-exclamation-circle"></i> Prioridad
                        </div>
                    </div>
                </div>
            </div>
            
            <div class="project-content" id="project-content">
                <!-- Content loaded based on sub-tab -->
            </div>
        </div>
    `;
    
    loadProjectContent();
}

function switchProjectTab(tab) {
    projectSubTab = tab;
    const projectId = selectedProjectId;
    window.location.hash = `project/${projectId}/${tab}`;
    
    document.querySelectorAll('.project-nav-tab').forEach(btn => {
        btn.classList.remove('active');
        if (btn.textContent.toLowerCase() === tab) btn.classList.add('active');
    });
    
    loadProjectContent();
}

function loadProjectContent() {
    const content = document.getElementById('project-content');
    if (!content) return;
    
    // Load project members
    loadProjectMembers();
    
    if (projectSubTab === 'dashboard') {
        // Load dashboard view
        loadProjectDashboard();
    } else if (projectSubTab === 'backlog') {
        // Load backlog view
        loadBacklog();
    } else if (projectSubTab === 'board') {
        // Load kanban board directly in project view
        loadProjectBoard();
    }
}

async function loadProjectDashboard() {
    console.log('=== loadProjectDashboard START ===');
    console.log('selectedProjectId:', selectedProjectId);
    
    const content = document.getElementById('project-content');
    if (!content || !selectedProjectId) {
        console.log('No content or selectedProjectId');
        return;
    }
    
    try {
        // Load dashboard HTML
        const response = await fetch('pages/dashboard.html');
        if (!response.ok) throw new Error(`Failed to load dashboard: ${response.status}`);
        const html = await response.text();
        content.innerHTML = html;
        
        // Load dashboard stats
        const stats = await apiRequest('/api/dashboard/stats');
        const statProjects = document.getElementById('stat-projects');
        const statStories = document.getElementById('stat-stories');
        const statTasks = document.getElementById('stat-tasks');
        if (statProjects) statProjects.textContent = stats.totalProjects || 0;
        if (statStories) statStories.textContent = stats.totalStories || 0;
        if (statTasks) statTasks.textContent = stats.completedTasks || 0;
        
        // Load stories for dashboard
        const stories = await apiRequest(`/api/stories/project/${selectedProjectId}/backlog`);
        renderDashboardStories(stories);
        
        // Calculate story points and completed
        const totalPoints = stories.reduce((sum, s) => sum + (s.storyPoints || 0), 0);
        const completed = stories.filter(s => s.status === 'Done').length;
        const statPoints = document.getElementById('stat-points');
        const statDone = document.getElementById('stat-done');
        if (statPoints) statPoints.textContent = totalPoints;
        if (statDone) statDone.textContent = completed;
        
        loadBurndownSprints();
    } catch (error) {
        console.error('Error loading project dashboard:', error);
        content.innerHTML = '<p class="empty-state">Error al cargar dashboard</p>';
    }
}

async function loadProjectBoard() {
    console.log('=== loadProjectBoard START ===');
    console.log('selectedProjectId:', selectedProjectId);
    
    const content = document.getElementById('project-content');
    if (!content || !selectedProjectId) {
        console.log('No content or selectedProjectId');
        return;
    }
    
    try {
        console.log('Fetching board data for project:', selectedProjectId);
        const data = await apiRequest(`/api/stories/project/${selectedProjectId}/board`);
        console.log('Board data received:', data);
        console.log('Stories count:', data.stories?.length || 0);
        console.log('Members count:', data.members?.length || 0);
        
        boardMembers = data.members || [];

        const noActiveSprint = data.hasActiveSprint === false;
        const boardHint = noActiveSprint
            ? `<div class="board-sprint-hint" role="status">
                    <i class="fas fa-info-circle"></i>
                    <span>No hay sprint activo. En el backlog, pulsa <strong>Iniciar sprint</strong> para que las historias aparezcan aquí.</span>
                </div>`
            : '';

        // Render kanban board HTML
        content.innerHTML = `${boardHint}
            <div class="kanban-board" id="kanban-board">
                <div class="kanban-column" data-status="Backlog" draggable="true">
                    <div class="kanban-column-header" onmousedown="startDragColumn(event, this)">
                        <i class="fas fa-grip-vertical column-drag-handle"></i>
                        <span class="column-title">Por hacer</span>
                        <span class="column-count" id="count-backlog">0</span>
                    </div>
                    <div class="kanban-column-content" id="column-backlog" ondrop="drop(event, 'Backlog')" ondragover="allowDrop(event)">
                    </div>
                </div>
                
                <div class="kanban-column" data-status="InProgress" draggable="true">
                    <div class="kanban-column-header" onmousedown="startDragColumn(event, this)">
                        <i class="fas fa-grip-vertical column-drag-handle"></i>
                        <span class="column-title">En curso</span>
                        <span class="column-count" id="count-in-progress">0</span>
                    </div>
                    <div class="kanban-column-content" id="column-in-progress" ondrop="drop(event, 'InProgress')" ondragover="allowDrop(event)">
                    </div>
                </div>
                
                <div class="kanban-column" data-status="Done" draggable="true">
                    <div class="kanban-column-header" onmousedown="startDragColumn(event, this)">
                        <i class="fas fa-grip-vertical column-drag-handle"></i>
                        <span class="column-title">Hecho</span>
                        <span class="column-count" id="count-done">0</span>
                    </div>
                    <div class="kanban-column-content" id="column-done" ondrop="drop(event, 'Done')" ondragover="allowDrop(event)">
                    </div>
                </div>
                
                <div class="add-column-btn" onclick="showAddColumnModal()">
                    <i class="fas fa-plus"></i>
                </div>
            </div>
        `;
        
        renderKanban(data.stories || [], data.members || []);
    } catch (error) {
        console.error('Error loading project board:', error);
        content.innerHTML = '<p class="empty-state">Error al cargar tablero</p>';
    }
}

async function loadProjectMembers() {
    console.log('loadProjectMembers called, selectedProjectId:', selectedProjectId);
    
    if (!selectedProjectId) {
        console.log('No selectedProjectId');
        return;
    }
    
    // Get project from projects array
    const project = projects.find(p => p.id === selectedProjectId);
    if (!project) {
        console.log('Project not found');
        return;
    }
    
    try {
        // Load members from API
        const members = await apiRequest(`/api/projects/${selectedProjectId}/members`);
        project.members = members;
        console.log('Loaded members:', members);
    } catch (error) {
        console.error('Error loading members:', error);
        project.members = [];
    }
    
    const membersContainer = document.getElementById('project-members-list');
    if (!membersContainer) {
        console.log('Members container not found');
        return;
    }

    const membersToShow = project.members.slice(0, 2);
    const remainingCount = project.members.length - 2;
    
    // Generate avatar HTML for up to 2 members with consistent colors
    let avatarsHtml = membersToShow.map((m, i) => {
        const initials = m.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2);
        const userColor = getUserColor(m.id);
        return `<div class="member-avatar" title="${escapeHtml(m.name)}" style="z-index: ${10-i}; margin-left: ${i > 0 ? '-8px' : '0'}; background: ${userColor}; color: white;">${initials}</div>`;
    }).join('');
    
    if (remainingCount > 0) {
        avatarsHtml += `<div class="member-avatar more-members" title="+${remainingCount} miembros más">+${remainingCount}</div>`;
    }
    
    console.log('Setting avatars HTML:', avatarsHtml);
    membersContainer.innerHTML = avatarsHtml;
}

// Temporary members list for new project
let projectMembersToAdd = [];

async function handleCreateProject(e) {
    e.preventDefault();
    
    const data = {
        name: document.getElementById('project-name').value,
        description: document.getElementById('project-description').value,
        key: document.getElementById('project-key')?.value || null,
        color: document.getElementById('project-color')?.value || null,
        icon: document.getElementById('project-icon')?.value || null,
        createdById: currentUser?.id,
        memberIds: projectMembersToAdd.map(m => m.id)
    };
    
    try {
        await apiRequest('/api/projects', { method: 'POST', body: JSON.stringify(data) });
        hideModal('project-modal');
        e.target.reset();
        projectMembersToAdd = [];
        renderMembersToAdd();
        loadProjects();
        showToast('Proyecto creado');
    } catch (error) {
        showToast('Error: ' + error.message, 'error');
    }
}

async function searchMember() {
    const email = document.getElementById('member-email').value.trim();
    if (!email) {
        showToast('Ingresa un correo electrónico', 'error');
        return;
    }
    
    // Check if already added
    if (projectMembersToAdd.find(m => m.email === email)) {
        showToast('Este miembro ya fue agregado', 'error');
        return;
    }
    
    try {
        // Search user by email
        const users = await apiRequest('/api/users');
        const user = users.find(u => u.email.toLowerCase() === email.toLowerCase());
        
        if (!user) {
            showToast('Usuario no encontrado', 'error');
            return;
        }
        
        // Add to list
        projectMembersToAdd.push(user);
        document.getElementById('member-email').value = '';
        renderMembersToAdd();
        showToast('Miembro agregado');
    } catch (error) {
        showToast('Error al buscar usuario', 'error');
    }
}

function renderMembersToAdd() {
    const container = document.getElementById('members-to-add');
    if (!container) return;
    
    if (projectMembersToAdd.length === 0) {
        container.innerHTML = '';
        return;
    }
    
    container.innerHTML = projectMembersToAdd.map((m, index) => {
        const initials = m.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2);
        return `
            <div class="member-item">
                <div class="member-avatar">${initials}</div>
                <div class="member-info">
                    <div class="member-name">${escapeHtml(m.name)}</div>
                    <div class="member-email">${escapeHtml(m.email)}</div>
                </div>
                <button type="button" class="btn btn-icon btn-small btn-remove" onclick="removeMemberToAdd(${index})">
                    <i class="fas fa-times"></i>
                </button>
            </div>
        `;
    }).join('');
}

function removeMemberToAdd(index) {
    projectMembersToAdd.splice(index, 1);
    renderMembersToAdd();
}

async function deleteProject(id, name) {
    projectToDelete = { id, name };
    document.getElementById('delete-project-name').textContent = name;
    showModal('delete-modal');
}

// Leave project function
let projectToLeave = null;

function leaveProject(id, name) {
    projectToLeave = { id, name };
    document.getElementById('leave-project-name').textContent = name;
    showModal('leave-modal');
}

// Open Add Members Modal
function openAddMembersModal(projectId, projectName) {
    currentAddMembersProjectId = projectId;
    document.getElementById('add-members-project-name').textContent = projectName;
    document.getElementById('member-email-search').value = '';
    document.getElementById('member-search-result').innerHTML = '';
    document.getElementById('members-to-add-section').style.display = 'none';
    membersToAdd = [];
    showModal('add-members-modal');
}

// Search member by email
async function searchMemberByEmail() {
    const email = document.getElementById('member-email-search').value.trim();
    if (!email) {
        showToast('Ingresa un email', 'error');
        return;
    }
    
    try {
        const response = await apiRequest(`/api/users/search?email=${encodeURIComponent(email)}`);
        if (response && response.id) {
            if (membersToAdd.find(m => m.id === response.id)) {
                showToast('Este usuario ya está en la lista', 'warning');
                return;
            }
            membersToAdd.push(response);
            renderMembersToAdd();
            document.getElementById('member-email-search').value = '';
        } else {
            showToast('Este usuario no está registrado en el sistema', 'error');
        }
    } catch (error) {
        showToast('Error: ' + error.message, 'error');
    }
}

// Render members to add list
function renderMembersToAdd() {
    const container = document.getElementById('members-to-add-list');
    const section = document.getElementById('members-to-add-section');
    
    if (membersToAdd.length === 0) {
        section.style.display = 'none';
        return;
    }
    
    section.style.display = 'block';
    container.innerHTML = membersToAdd.map((m, index) => `
        <div class="member-to-add-item">
            <span>${escapeHtml(m.name)} (${escapeHtml(m.email)})</span>
            <button type="button" class="btn-icon" onclick="removeMemberToAdd(${index})" title="Quitar">
                <i class="fas fa-times"></i>
            </button>
        </div>
    `).join('');
}

// Remove member from to-add list
function removeMemberToAdd(index) {
    membersToAdd.splice(index, 1);
    renderMembersToAdd();
}

// Confirm add members
async function confirmAddMembers() {
    if (!currentAddMembersProjectId || membersToAdd.length === 0) {
        showToast('No hay miembros para agregar', 'warning');
        return;
    }
    
    try {
        for (const member of membersToAdd) {
            await apiRequest(`/api/projects/${currentAddMembersProjectId}/members`, {
                method: 'POST',
                body: JSON.stringify({ userId: member.id })
            });
        }
        
        showToast(`${membersToAdd.length} invitacion(es) enviada(s). El usuario debe aceptar en notificaciones.`);
        hideModal('add-members-modal');
        membersToAdd = [];
        loadProjects(); // Refresh to show new members
    } catch (error) {
        showToast('Error: ' + error.message, 'error');
    }
}

// Escape HTML helper
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Notification functions
let notifications = [];

async function loadNotifications() {
    if (!currentUser?.id) return;
    
    try {
        notifications = await apiRequest(`/api/notifications?userId=${currentUser.id}`);
        updateNotificationBadge();
    } catch (error) {
        console.error('Error loading notifications:', error);
    }
}

async function updateNotificationBadge() {
    try {
        const result = await apiRequest(`/api/notifications/unread-count?userId=${currentUser.id}`);
        const badge = document.getElementById('notification-badge');
        if (badge) {
            if (result.count > 0) {
                badge.textContent = result.count > 99 ? '99+' : result.count;
                badge.style.display = 'flex';
            } else {
                badge.style.display = 'none';
            }
        }
    } catch (error) {
        console.error('Error updating badge:', error);
    }
}

function openNotifications() {
    renderNotifications();
    showModal('notifications-modal');
}

function renderNotifications() {
    const container = document.getElementById('notifications-list');
    if (!container) return;
    
    if (notifications.length === 0) {
        container.innerHTML = '<p class="text-muted text-center">No tienes notificaciones</p>';
        return;
    }
    
    container.innerHTML = notifications.map(n => {
        const timeAgo = getTimeAgo(n.createdAt);
        const unreadClass = !n.isRead ? 'unread' : '';
        const isPendingInvitation = n.type === 'project_invitation' && n.status === 'pending';
        
        let actionButtons = '';
        if (isPendingInvitation) {
            actionButtons = `
                <div class="notification-actions">
                    <button class="btn btn-small btn-primary" onclick="acceptInvitation('${n.id}', event)">
                        <i class="fas fa-check"></i> Aceptar
                    </button>
                    <button class="btn btn-small btn-danger" onclick="rejectInvitation('${n.id}', event)">
                        <i class="fas fa-times"></i> Rechazar
                    </button>
                </div>
            `;
        } else if (n.status === 'accepted') {
            actionButtons = `<div class="notification-status accepted"><i class="fas fa-check-circle"></i> Aceptada</div>`;
        } else if (n.status === 'rejected') {
            actionButtons = `<div class="notification-status rejected"><i class="fas fa-times-circle"></i> Rechazada</div>`;
        }
        
        return `
            <div class="notification-item ${unreadClass}">
                <div class="notification-item-header">
                    <span class="notification-item-title">${escapeHtml(n.title)}</span>
                    <span class="notification-item-time">${timeAgo}</span>
                </div>
                <div class="notification-item-message">${escapeHtml(n.message)}</div>
                ${n.creatorName ? `<div class="notification-item-creator">👤 Invitado por: ${escapeHtml(n.creatorName)}</div>` : ''}
                ${n.projectName ? `<div class="notification-item-project">📁 ${escapeHtml(n.projectName)}</div>` : ''}
                ${actionButtons}
            </div>
        `;
    }).join('');
}

async function markAsRead(id) {
    try {
        await apiRequest(`/api/notifications/${id}/read`, { method: 'PUT' });
        const notification = notifications.find(n => n.id === id);
        if (notification) notification.isRead = true;
        renderNotifications();
        updateNotificationBadge();
    } catch (error) {
        console.error('Error marking as read:', error);
    }
}

async function acceptInvitation(id, event) {
    event.stopPropagation();
    try {
        await apiRequest(`/api/notifications/${id}/accept`, { method: 'POST' });
        const notification = notifications.find(n => n.id === id);
        if (notification) {
            notification.status = 'accepted';
            notification.isRead = true;
        }
        renderNotifications();
        updateNotificationBadge();
        showToast('Invitación aceptada. Ahora eres miembro del proyecto.');
        loadProjects(); // Reload projects to show the new project
    } catch (error) {
        console.error('Error accepting invitation:', error);
        showToast('Error al aceptar invitación', 'error');
    }
}

async function rejectInvitation(id, event) {
    event.stopPropagation();
    try {
        await apiRequest(`/api/notifications/${id}/reject`, { method: 'POST' });
        const notification = notifications.find(n => n.id === id);
        if (notification) {
            notification.status = 'rejected';
            notification.isRead = true;
        }
        renderNotifications();
        updateNotificationBadge();
        showToast('Invitación rechazada');
    } catch (error) {
        console.error('Error rejecting invitation:', error);
        showToast('Error al rechazar invitación', 'error');
    }
}

async function markAllAsRead() {
    if (!currentUser?.id) return;
    
    try {
        await apiRequest(`/api/notifications/read-all?userId=${currentUser.id}`, { method: 'PUT' });
        notifications.forEach(n => n.isRead = true);
        renderNotifications();
        updateNotificationBadge();
        showToast('Todas las notificaciones marcadas como leídas');
    } catch (error) {
        console.error('Error marking all as read:', error);
    }
}

function getTimeAgo(dateString) {
    const date = new Date(dateString);
    const now = new Date();
    const seconds = Math.floor((now - date) / 1000);
    
    if (seconds < 60) return 'ahora';
    if (seconds < 3600) return `hace ${Math.floor(seconds / 60)} min`;
    if (seconds < 86400) return `hace ${Math.floor(seconds / 3600)} h`;
    if (seconds < 604800) return `hace ${Math.floor(seconds / 86400)} días`;
    return date.toLocaleDateString();
}

// Confirm delete button handler
document.addEventListener('DOMContentLoaded', () => {
    const confirmBtn = document.getElementById('confirm-delete-btn');
    if (confirmBtn) {
        confirmBtn.addEventListener('click', () => {
            if (!projectToDelete) return;
            
            apiRequest(`/api/projects/${projectToDelete.id}?userId=${currentUser.id}`, { method: 'DELETE' })
                .then(() => {
                    showToast(`Proyecto "${projectToDelete.name}" eliminado`);
                    hideModal('delete-modal');
                    projectToDelete = null;
                    loadProjects();
                })
                .catch(err => showToast('Error: ' + err.message, 'error'));
        });
    }

    // Confirm delete story button handler
    const confirmDeleteStoryBtn = document.getElementById('confirm-delete-story-btn');
    if (confirmDeleteStoryBtn) {
        confirmDeleteStoryBtn.addEventListener('click', confirmDeleteStory);
    }

    // Confirm leave button handler
    const confirmLeaveBtn = document.getElementById('confirm-leave-btn');
    if (confirmLeaveBtn) {
        confirmLeaveBtn.addEventListener('click', () => {
            if (!projectToLeave) return;
            
            apiRequest(`/api/projects/${projectToLeave.id}/leave?userId=${currentUser.id}`, { method: 'POST' })
                .then(() => {
                    showToast(`Has salido del proyecto "${projectToLeave.name}"`);
                    hideModal('leave-modal');
                    projectToLeave = null;
                    loadProjects();
                })
                .catch(err => showToast('Error: ' + err.message, 'error'));
        });
    }
});

let projectToDelete = null;

function populateProjectSelects() {
    // Reservado: antes se usaba #story-project como select (incorrecto). Los selects de proyecto viven en cada pantalla.
}

async function loadBacklog() {
    console.log('loadBacklog called, selectedProjectId:', selectedProjectId);
    
    const content = document.getElementById('project-content');
    if (!content || !selectedProjectId) {
        console.log('No content or selectedProjectId');
        return;
    }
    
    try {
        console.log('Fetching sprints and stories for project:', selectedProjectId);
        
        // 1. Obtener sprints del proyecto
        sprints = await apiRequest(`/api/sprints/project/${selectedProjectId}`);
        console.log('Sprints received:', sprints);
        
        // 2. Obtener TODAS las historias del proyecto (incluyendo las de sprints)
        stories = await apiRequest(`/api/stories/project/${selectedProjectId}`);
        console.log('All stories received:', stories);
        
        // 3. Separar historias: backlog (sin sprint) y las que están en sprints
        const backlogStories = stories.filter(s => !s.sprintId || s.sprintId === '');
        const sprintStories = stories.filter(s => s.sprintId && s.sprintId !== '');
        
        console.log('Backlog stories:', backlogStories.length, 'Sprint stories:', sprintStories.length);
        
        const project = projects.find(p => p.id === selectedProjectId);
        
        // 4. Renderizar backlog con sprints e historias
        renderProjectBacklog(sprints || [], stories || [], project?.members || []);
    } catch (error) {
        console.error('Error loading project backlog:', error);
        content.innerHTML = '<p class="empty-state">Error al cargar backlog</p>';
    }
}

function renderProjectBacklog(sprints, stories, members) {
    const content = document.getElementById('project-content');
    if (!content) return;
    
    // Get project key
    const project = projects.find(p => p.id === selectedProjectId);
    let projectKey;
    if (project) {
        if (project.key) {
            projectKey = project.key;
        } else {
            // Generate key from project name (first 4 letters) or use PROJ
            const nameParts = project.name.split(' ');
            if (nameParts.length > 1) {
                // Take first letter of each word (up to 4)
                projectKey = nameParts.map(p => p[0]).join('').toUpperCase().substring(0, 4);
            } else {
                // Take first 4 letters of single word
                projectKey = project.name.substring(0, 4).toUpperCase();
            }
        }
    } else {
        projectKey = 'PROJ';
    }
    
    // Jira-like split: real sprints live above, unscheduled work stays in backlog.
    const sprint1Stories = [];
    const backlogStories = stories.filter(s => (!s.sprintId || s.sprintId === '') && s.status === 'Backlog');
    const inProgressStories = stories.filter(s => s.status === 'InProgress');
    const doneStories = stories.filter(s => s.status === 'Done');
    
    // Calculate story points
    const backlogPoints = backlogStories.reduce((sum, s) => sum + (s.storyPoints || 0), 0);
    const inProgressPoints = inProgressStories.reduce((sum, s) => sum + (s.storyPoints || 0), 0);
    const donePoints = doneStories.reduce((sum, s) => sum + (s.storyPoints || 0), 0);
    
    // Generate HTML for sprints dinámicos (excluyendo Sprint 1 que se maneja separado)
    let dynamicSprintsHtml = '';
    if (sprints && sprints.length > 0) {
        // Filtrar sprints que no sean Sprint 1 y ordenar por fecha de creación
        const dynamicSprints = sprints
            .sort((a, b) => new Date(a.createdAt) - new Date(b.createdAt));
        
        dynamicSprints.forEach(sprint => {
            // Obtener historias asociadas a este sprint
            const sprintStories = stories.filter(s => s.sprintId === sprint.id);
            const sprintStoryCount = sprintStories.length;
            
            // Calcular puntos por estado
            const sprintTodo = sprintStories.filter(s => s.status === 'Backlog').length;
            const sprintInProgress = sprintStories.filter(s => s.status === 'InProgress').length;
            const sprintDone = sprintStories.filter(s => s.status === 'Done').length;
            
            const sprintId = `sprint-${sprint.id}`;
            
            dynamicSprintsHtml += `
                <div class="sprint-section" data-drop-target-for-element="true" id="${sprintId}" data-sprint-id="${sprint.id}">
                    <div class="backlog-section-header">
                        <div class="backlog-header-left">
                            <label class="backlog-checkbox-label">
                                <input type="checkbox" class="backlog-checkbox" aria-label="Seleccionar todas las actividades en sprint">
                                <svg width="20" height="20" viewBox="0 0 24 24" role="presentation">
                                    <g fill-rule="evenodd">
                                        <rect fill="currentColor" x="5.5" y="5.5" width="13" height="13" rx="1.5"></rect>
                                    </g>
                                </svg>
                            </label>
                            <button class="backlog-collapse-btn" aria-expanded="true" onclick="toggleDynamicSprintSection(this, '${sprintId}')" title="Contraer">
                                <svg fill="none" viewBox="0 0 16 16" role="presentation" style="transform: rotate(0deg); transition: transform 0.2s;">
                                    <path fill="currentcolor" d="m14.53 6.03-6 6a.75.75 0 0 1-1.004.052l-.056-.052-6-6 1.06-1.06L8 10.44l5.47-5.47z"></path>
                                </svg>
                            </button>
                            <span class="backlog-section-title">${escapeHtml(sprint.name)} <span class="activity-count">(${sprintStoryCount} actividades)</span></span>
                        </div>
                        <div class="backlog-header-right">
                            <div class="backlog-badges">
                                <div class="backlog-badge todo" title="Pendiente">${sprintTodo}</div>
                                <div class="backlog-badge inprogress" title="En curso">${sprintInProgress}</div>
                                <div class="backlog-badge done" title="Hecho">${sprintDone}</div>
                            </div>
                            <button type="button" class="create-sprint-btn ${sprint.status === 'Active' ? 'btn-complete' : ''}" onclick="${sprint.status === 'Active' ? `completeSprint('${sprint.id}')` : `showStartSprintModalForSprint('${sprintId}')`}" ${sprintStoryCount === 0 && sprint.status !== 'Active' ? 'disabled' : ''}>${sprint.status === 'Active' ? 'Completar sprint' : 'Iniciar sprint'}</button>
                            <div class="sprint-menu-container">
                                <button type="button" class="sprint-menu-btn" onclick="toggleSprintOptionsMenu(event, '${sprintId}')" title="Más opciones">
                                    <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                        <path fill="currentcolor" fill-rule="evenodd" d="M8 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4M6 8a1 1 0 1 1 2 0 1 1 0 0 1-2 0m5-2a2 2 0 1 1 0 4 2 2 0 0 1 0-4m0 1a1 1 0 1 0 0 2 1 1 0 0 0 0-2M4 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4m0 1a1 1 0 1 1 0 2 1 1 0 0 1 0-2" clip-rule="evenodd"></path>
                                    </svg>
                                </button>
                                <div class="sprint-options-menu" id="sprint-options-menu-${sprintId}" style="display: none;" onclick="event.stopPropagation()">
                                    <button class="sprint-option-item" onclick="event.stopPropagation(); editSprint('${sprintId}');">
                                        <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                            <path fill="currentcolor" fill-rule="evenodd" d="M11.586.854a2 2 0 0 1 2.828 0l.732.732a2 2 0 0 1 0 2.828L10.01 9.551a2 2 0 0 1-.864.51l-3.189.91a.75.75 0 0 1-.927-.927l.91-3.189a2 2 0 0 1 .51-.864zm1.768 1.06a.5.5 0 0 0-.708 0l-.585.586L13.5 3.94l.586-.586a.5.5 0 0 0 0-.708zM12.439 5 11 3.56 7.51 7.052a.5.5 0 0 0-.128.216l-.54 1.891 1.89-.54a.5.5 0 0 0 .217-.127zM3 2.501a.5.5 0 0 0-.5.5v10a.5.5 0 0 0 .5.5h10a.5.5 0 0 0 .5-.5V10H15v3.001a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2v-10a2 2 0 0 1 2-2h3v1.5z" clip-rule="evenodd"></path>
                                        </svg>
                                        Editar sprint
                                    </button>
                                    <button class="sprint-option-item delete" onclick="handleDeleteSprintClick(event, '${sprintId}')">
                                        <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                            <path fill="currentcolor" fill-rule="evenodd" d="M10 2a1 1 0 0 1 1 1v1h2.5a.5.5 0 0 1 0 1h-.538l-.566 8.486A2.5 2.5 0 0 1 9.9 15.6H6.1a2.5 2.5 0 0 1-2.496-2.114L3.038 5H2.5a.5.5 0 0 1 0-1H5V3a1 1 0 0 1 1-1h4m-5 3h9l-.56 8.397a1.5 1.5 0 0 1-1.498 1.268H6.057a1.5 1.5 0 0 1-1.498-1.268zM9 3H7v1h2z" clip-rule="evenodd"></path>
                                        </svg>
                                        Eliminar sprint
                                    </button>
                                </div>
                            </div>
                        </div>
                    </div>
                    <div class="sprint-content" id="sprint-content-${sprintId}" ondragover="allowBacklogDrop(event)" ondrop="dropStoryOnSprint(event, '${sprintId}')">
                        ${sprintStories.length > 0 
                            ? sprintStories.map(story => createBacklogStoryCard(story, members, projectKey)).join('')
                            : `<div class="sprint-empty-message" style="padding: 24px; text-align: center; color: var(--text-muted); border: 2px dashed var(--border-color); border-radius: 8px; margin: 8px 0;">
                                <i class="fas fa-hand-pointer" style="font-size: 24px; margin-bottom: 8px; display: block;"></i>
                                <span>Planifica un sprint arrastrando actividades hasta él o arrastrando el pie de página del sprint.</span>
                               </div>`
                        }
                    </div>
                    <div class="backlog-nudge-container" id="create-story-container-${sprintId}">
                        <div class="create-story-form" id="create-story-form-${sprintId}" style="display: none;">
                            <div class="create-form-row">
                                <label class="story-checkbox-label">
                                    <input type="checkbox" class="story-checkbox" aria-label="Seleccionar esta historia">
                                    <svg width="24" height="24" viewBox="0 0 24 24" role="presentation">
                                        <g fill-rule="evenodd">
                                            <rect fill="currentColor" x="5.5" y="5.5" width="13" height="13" rx="1.5"></rect>
                                        </g>
                                    </svg>
                                </label>
                                <input type="text" class="story-title-input" aria-label="Work item summary" maxlength="255" placeholder="Describe qué hay que hacer." id="new-story-title-${sprintId}">
                                <button class="due-date-btn" aria-label="Fecha de vencimiento" type="button">
                                    <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                        <path fill="currentcolor" fill-rule="evenodd" d="M4.5 2.5v2H6v-2h4v2h1.5v-2H13a.5.5 0 0 1 .5.5v3h-11V3a.5.5 0 0 1 .5-.5zm-2 5V13a.5.5 0 0 0 .5.5h10a.5.5 0 0 0 .5-.5V7.5zm9-6.5H13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V3a2 2 0 0 1 2-2h1.5V0H6v1h4V0h1.5z" clip-rule="evenodd"></path>
                                    </svg>
                                </button>
                                <button class="assignee-btn" aria-label="Sin asignar" type="button">
                                    <div class="assignee-avatar">
                                        <svg fill="none" viewBox="-4 -4 24 24" role="presentation">
                                            <path fill="currentcolor" fill-rule="evenodd" d="M8 1.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5M4 4a4 4 0 1 1 8 0 4 4 0 0 1-8 0m-2 9a3.75 3.75 0 0 1 3.75-3.75h4.5A3.75 3.75 0 0 1 14 13v2h-1.5v-2a2.25 2.25 0 0 0-2.25-2.25h-4.5A2.25 2.25 0 0 0 3.5 13v2H2z" clip-rule="evenodd"></path>
                                        </svg>
                                    </div>
                                </button>
                                <button class="create-submit-btn" type="button" disabled onclick="createStoryForSprint('${sprintId}')">
                                    <span>Crear</span>
                                    <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                        <path fill="currentcolor" fill-rule="evenodd" d="M12.5 8V3H14v5.438c0 .586-.476 1.062-1.062 1.062H4.56l2.72 2.72-1.061 1.06-4-4a.75.75 0 0 1 0-1.06l4-4 1.06 1.06L4.56 8z" clip-rule="evenodd"></path>
                                    </svg>
                                </button>
                            </div>
                        </div>
                        <div class="create-story-trigger" id="create-story-trigger-${sprintId}">
                            <button class="create-story-btn" onclick="showCreateStoryFormForSprint('${sprintId}')">
                                <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                    <path fill="currentcolor" fill-rule="evenodd" d="M7.25 8.75V15h1.5V8.75H15v-1.5H8.75V1h-1.5v6.25H1v1.5z" clip-rule="evenodd"></path>
                                </svg>
                                Crear
                            </button>
                        </div>
                    </div>
                </div>
            `;
        });
    }
    
    content.innerHTML = `
        <div class="backlog-container">
            <!-- Sprint 1 Section -->
            <div class="sprint-section" data-drop-target-for-element="true">
                <div class="backlog-section-header">
                    <div class="backlog-header-left">
                        <label class="backlog-checkbox-label">
                            <input type="checkbox" class="backlog-checkbox" aria-label="Seleccionar todas las actividades en sprint">
                            <svg width="20" height="20" viewBox="0 0 24 24" role="presentation">
                                <g fill-rule="evenodd">
                                    <rect fill="currentColor" x="5.5" y="5.5" width="13" height="13" rx="1.5"></rect>
                                </g>
                            </svg>
                        </label>
                        <button class="backlog-collapse-btn" aria-expanded="true" onclick="toggleSprintSection(this)" title="Contraer">
                            <svg fill="none" viewBox="0 0 16 16" role="presentation" style="transform: rotate(0deg); transition: transform 0.2s;">
                                <path fill="currentcolor" d="m14.53 6.03-6 6a.75.75 0 0 1-1.004.052l-.056-.052-6-6 1.06-1.06L8 10.44l5.47-5.47z"></path>
                            </svg>
                        </button>
                        <span class="backlog-section-title">Sprint 1 <span class="activity-count">(${sprint1Stories.length} actividades)</span></span>
                    </div>
                    <div class="backlog-header-right">
                        <div class="backlog-badges">
                            <div class="backlog-badge todo" title="Pendiente">${sprint1Stories.filter(s => s.status === 'Backlog').length}</div>
                            <div class="backlog-badge inprogress" title="En curso">${sprint1Stories.filter(s => s.status === 'InProgress').length}</div>
                            <div class="backlog-badge done" title="Hecho">${sprint1Stories.filter(s => s.status === 'Done').length}</div>
                        </div>
                        <button type="button" class="create-sprint-btn" onclick="showStartSprintModalForSprint('sprint-1')" ${sprint1Stories.length === 0 ? 'disabled' : ''}>Iniciar sprint</button>
                        <div class="sprint-menu-container">
                            <button type="button" class="sprint-menu-btn" onclick="toggleSprintOptionsMenu(event, 'sprint-1')" title="Más opciones">
                                <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                    <path fill="currentcolor" fill-rule="evenodd" d="M8 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4M6 8a1 1 0 1 1 2 0 1 1 0 0 1-2 0m5-2a2 2 0 1 1 0 4 2 2 0 0 1 0-4m0 1a1 1 0 1 0 0 2 1 1 0 0 0 0-2M4 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4m0 1a1 1 0 1 1 0 2 1 1 0 0 1 0-2" clip-rule="evenodd"></path>
                                </svg>
                            </button>
                            <div class="sprint-options-menu" id="sprint-options-menu-sprint-1" style="display: none;" onclick="event.stopPropagation()">
                                <button class="sprint-option-item" onclick="event.stopPropagation(); editSprint('sprint-1');">
                                    <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                        <path fill="currentcolor" fill-rule="evenodd" d="M11.586.854a2 2 0 0 1 2.828 0l.732.732a2 2 0 0 1 0 2.828L10.01 9.551a2 2 0 0 1-.864.51l-3.189.91a.75.75 0 0 1-.927-.927l.91-3.189a2 2 0 0 1 .51-.864zm1.768 1.06a.5.5 0 0 0-.708 0l-.585.586L13.5 3.94l.586-.586a.5.5 0 0 0 0-.708zM12.439 5 11 3.56 7.51 7.052a.5.5 0 0 0-.128.216l-.54 1.891 1.89-.54a.5.5 0 0 0 .217-.127zM3 2.501a.5.5 0 0 0-.5.5v10a.5.5 0 0 0 .5.5h10a.5.5 0 0 0 .5-.5V10H15v3.001a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2v-10a2 2 0 0 1 2-2h3v1.5z" clip-rule="evenodd"></path>
                                    </svg>
                                    Editar sprint
                                </button>
                                <button class="sprint-option-item delete" onclick="handleDeleteSprintClick(event, 'sprint-1')">
                                    <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                        <path fill="currentcolor" fill-rule="evenodd" d="M10 2a1 1 0 0 1 1 1v1h2.5a.5.5 0 0 1 0 1h-.538l-.566 8.486A2.5 2.5 0 0 1 9.9 15.6H6.1a2.5 2.5 0 0 1-2.496-2.114L3.038 5H2.5a.5.5 0 0 1 0-1H5V3a1 1 0 0 1 1-1h4m-5 3h9l-.56 8.397a1.5 1.5 0 0 1-1.498 1.268H6.057a1.5 1.5 0 0 1-1.498-1.268zM9 3H7v1h2z" clip-rule="evenodd"></path>
                                    </svg>
                                    Eliminar sprint
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
                <div class="sprint-content" id="sprint-content">
                    <!-- Stories will be rendered here -->
                </div>
                <div class="backlog-nudge-container" id="create-story-container">
                    <div class="create-story-form" id="create-story-form" style="display: none;">
                        <div class="create-form-row">
                            <label class="story-checkbox-label">
                                <input type="checkbox" class="story-checkbox" aria-label="Seleccionar esta historia">
                                <svg width="24" height="24" viewBox="0 0 24 24" role="presentation">
                                    <g fill-rule="evenodd">
                                        <rect fill="currentColor" x="5.5" y="5.5" width="13" height="13" rx="1.5"></rect>
                                    </g>
                                </svg>
                            </label>
                            <input type="text" class="story-title-input" aria-label="Work item summary" maxlength="255" placeholder="Describe qué hay que hacer." id="new-story-title">
                            <button class="due-date-btn" aria-label="Fecha de vencimiento" type="button">
                                <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                    <path fill="currentcolor" fill-rule="evenodd" d="M4.5 2.5v2H6v-2h4v2h1.5v-2H13a.5.5 0 0 1 .5.5v3h-11V3a.5.5 0 0 1 .5-.5zm-2 5V13a.5.5 0 0 0 .5.5h10a.5.5 0 0 0 .5-.5V7.5zm9-6.5H13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V3a2 2 0 0 1 2-2h1.5V0H6v1h4V0h1.5z" clip-rule="evenodd"></path>
                                </svg>
                            </button>
                            <button class="assignee-btn" aria-label="Sin asignar" type="button">
                                <div class="assignee-avatar">
                                    <svg fill="none" viewBox="-4 -4 24 24" role="presentation">
                                        <path fill="currentcolor" fill-rule="evenodd" d="M8 1.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5M4 4a4 4 0 1 1 8 0 4 4 0 0 1-8 0m-2 9a3.75 3.75 0 0 1 3.75-3.75h4.5A3.75 3.75 0 0 1 14 13v2h-1.5v-2a2.25 2.25 0 0 0-2.25-2.25h-4.5A2.25 2.25 0 0 0 3.5 13v2H2z" clip-rule="evenodd"></path>
                                    </svg>
                                </div>
                            </button>
                            <button class="create-submit-btn" type="button" disabled onclick="createStoryForSprint('sprint-1')">
                                <span>Crear</span>
                                <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                    <path fill="currentcolor" fill-rule="evenodd" d="M12.5 8V3H14v5.438c0 .586-.476 1.062-1.062 1.062H4.56l2.72 2.72-1.061 1.06-4-4a.75.75 0 0 1 0-1.06l4-4 1.06 1.06L4.56 8z" clip-rule="evenodd"></path>
                                </svg>
                            </button>
                        </div>
                    </div>
                    <div class="create-story-trigger" id="create-story-trigger">
                        <button class="create-story-btn" onclick="showCreateStoryForm()">
                            <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                <path fill="currentcolor" fill-rule="evenodd" d="M7.25 8.75V15h1.5V8.75H15v-1.5H8.75V1h-1.5v6.25H1v1.5z" clip-rule="evenodd"></path>
                            </svg>
                            Crear
                        </button>
                    </div>
                </div>
            </div>
            
            <!-- Dynamic Sprints -->
            ${dynamicSprintsHtml}
            
            <!-- Backlog Section -->
            <div class="backlog-section" data-drop-target-for-element="true">
                <div class="backlog-section-header">
                    <div class="backlog-header-left">
                        <label class="backlog-checkbox-label">
                            <input type="checkbox" class="backlog-checkbox" aria-label="Seleccionar todas las actividades en backlog">
                            <svg width="20" height="20" viewBox="0 0 24 24" role="presentation">
                                <g fill-rule="evenodd">
                                    <rect fill="currentColor" x="5.5" y="5.5" width="13" height="13" rx="1.5"></rect>
                                </g>
                            </svg>
                        </label>
                        <button class="backlog-collapse-btn" aria-expanded="true" onclick="toggleBacklogSection(this)" title="Contraer">
                            <svg fill="none" viewBox="0 0 16 16" role="presentation" style="transform: rotate(0deg); transition: transform 0.2s;">
                                <path fill="currentcolor" d="m14.53 6.03-6 6a.75.75 0 0 1-1.004.052l-.056-.052-6-6 1.06-1.06L8 10.44l5.47-5.47z"></path>
                            </svg>
                        </button>
                        <span class="backlog-section-title">Backlog <span class="activity-count">(${backlogStories.length} actividades)</span></span>
                    </div>
                    <div class="backlog-header-right">
                        <div class="backlog-badges">
                            <div class="backlog-badge todo" title="Pendiente">${backlogPoints}</div>
                            <div class="backlog-badge inprogress" title="En curso">${inProgressPoints}</div>
                            <div class="backlog-badge done" title="Hecho">${donePoints}</div>
                        </div>
                        <button type="button" class="create-sprint-btn" onclick="createSprintFromBacklog()">Crear sprint</button>
                    </div>
                </div>
                <div class="backlog-content" id="backlog-content" ondragover="allowBacklogDrop(event)" ondrop="dropStoryOnBacklog(event)">
                    <!-- Backlog stories will be rendered here -->
                </div>
                <div class="backlog-nudge-container" id="backlog-create-container">
                    <div class="create-story-form" id="backlog-create-story-form" style="display: none;">
                        <div class="create-form-row">
                            <label class="story-checkbox-label">
                                <input type="checkbox" class="story-checkbox" aria-label="Seleccionar esta historia">
                                <svg width="24" height="24" viewBox="0 0 24 24" role="presentation">
                                    <g fill-rule="evenodd">
                                        <rect fill="currentColor" x="5.5" y="5.5" width="13" height="13" rx="1.5"></rect>
                                    </g>
                                </svg>
                            </label>
                            <input type="text" class="story-title-input" aria-label="Work item summary" maxlength="255" placeholder="Describe qué hay que hacer." id="backlog-new-story-title">
                            <button class="due-date-btn" aria-label="Fecha de vencimiento" type="button">
                                <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                    <path fill="currentcolor" fill-rule="evenodd" d="M4.5 2.5v2H6v-2h4v2h1.5v-2H13a.5.5 0 0 1 .5.5v3h-11V3a.5.5 0 0 1 .5-.5zm-2 5V13a.5.5 0 0 0 .5.5h10a.5.5 0 0 0 .5-.5V7.5zm9-6.5H13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V3a2 2 0 0 1 2-2h1.5V0H6v1h4V0h1.5z" clip-rule="evenodd"></path>
                                </svg>
                            </button>
                            <button class="assignee-btn" aria-label="Sin asignar" type="button">
                                <div class="assignee-avatar">
                                    <svg fill="none" viewBox="-4 -4 24 24" role="presentation">
                                        <path fill="currentcolor" fill-rule="evenodd" d="M8 1.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5M4 4a4 4 0 1 1 8 0 4 4 0 0 1-8 0m-2 9a3.75 3.75 0 0 1 3.75-3.75h4.5A3.75 3.75 0 0 1 14 13v2h-1.5v-2a2.25 2.25 0 0 0-2.25-2.25h-4.5A2.25 2.25 0 0 0 3.5 13v2H2z" clip-rule="evenodd"></path>
                                    </svg>
                                </div>
                            </button>
                            <button class="create-submit-btn" type="button" disabled>
                                <span>Crear</span>
                                <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                    <path fill="currentcolor" fill-rule="evenodd" d="M12.5 8V3H14v5.438c0 .586-.476 1.062-1.062 1.062H4.56l2.72 2.72-1.061 1.06-4-4a.75.75 0 0 1 0-1.06l4-4 1.06 1.06L4.56 8z" clip-rule="evenodd"></path>
                                </svg>
                            </button>
                        </div>
                    </div>
                    <div class="create-story-trigger" id="backlog-create-story-trigger">
                        <button class="create-story-btn" onclick="showCreateStoryFormBacklog()">
                            <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                <path fill="currentcolor" fill-rule="evenodd" d="M7.25 8.75V15h1.5V8.75H15v-1.5H8.75V1h-1.5v6.25H1v1.5z" clip-rule="evenodd"></path>
                            </svg>
                            Crear
                        </button>
                    </div>
                </div>
            </div>
        </div>
    `;
    
    // Render stories in sprint content (Sprint 1 - only stories without sprintId)
    const sprintContent = document.getElementById('sprint-content');
    if (sprintContent) {
        if (sprint1Stories.length > 0) {
            sprintContent.innerHTML = sprint1Stories.map(story => createBacklogStoryCard(story, members, projectKey)).join('');
        } else {
            sprintContent.innerHTML = `<div class="sprint-empty-message" style="padding: 24px; text-align: center; color: var(--text-muted); border: 2px dashed var(--border-color); border-radius: 8px; margin: 8px 0;">
                <i class="fas fa-hand-pointer" style="font-size: 24px; margin-bottom: 8px; display: block;"></i>
                <span>Planifica un sprint arrastrando actividades hasta él o arrastrando el pie de página del sprint.</span>
            </div>`;
        }
    }
    
    // Render backlog stories
    const backlogContent = document.getElementById('backlog-content');
    if (backlogContent) {
        if (backlogStories.length > 0) {
            backlogContent.innerHTML = backlogStories.map(story => createBacklogStoryCard(story, members, projectKey)).join('');
        } else {
            backlogContent.innerHTML = `<div class="backlog-empty-message" style="padding: 24px; text-align: center; color: var(--text-muted); border: 2px dashed var(--border-color); border-radius: 8px; margin: 8px 0;">
                <i class="fas fa-clipboard-list" style="font-size: 24px; margin-bottom: 8px; display: block;"></i>
                <span>Tu Backlog está vacío</span>
            </div>`;
        }
    }

    const conceptualSprint = content.querySelector('.sprint-section:not([data-sprint-id])');
    if (conceptualSprint) conceptualSprint.remove();
}

function createBacklogStoryCard(story, members, projectKey) {
    const assignee = members.find(m => m.id === story.assigneeId);
    const assigneeInitials = assignee ? assignee.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2) : '?';
    const assigneeColor = assignee ? getUserColor(assignee.id) : null;
    
    const priorityColors = {
        1: '#ff6b6b', // Alta - rojo
        2: '#feca57', // Media - amarillo  
        3: '#48dbfb', // Baja - azul
    };
    
    const priorityColor = priorityColors[story.priority] || '#95a5a6';
    // Extract story number - use last 3-4 chars of ID or generate sequential number
    const storyNum = story.id.length > 4 ? story.id.slice(-3) : story.id.substring(0, 3);
    const storyNumber = storyNum.toUpperCase();
    
    // Get status label
    const statusLabels = {
        'Backlog': 'TAREAS POR HACER',
        'InProgress': 'EN PROGRESO',
        'Done': 'COMPLETADO',
        'SprintBacklog': 'SPRINT BACKLOG'
    };
    const statusLabel = statusLabels[story.status] || 'TAREAS POR HACER';
    
    return `
        <div class="backlog-story-card" data-story-id="${story.id}" draggable="true" ondragstart="dragBacklogStory(event, '${story.id}')">
            <div class="story-left">
                <label class="story-checkbox-label">
                    <input type="checkbox" class="story-checkbox" id="story-${story.id}">
                    <svg width="20" height="20" viewBox="0 0 24 24" role="presentation">
                        <g fill-rule="evenodd">
                            <rect fill="currentColor" x="5.5" y="5.5" width="13" height="13" rx="1.5"></rect>
                        </g>
                    </svg>
                </label>
                <div class="story-main">
                    <div class="story-title-row">
                        <span class="story-key">${projectKey}-${storyNumber}</span>
                        <span class="story-title">${escapeHtml(story.title)}</span>
                        <button class="story-edit-icon" onclick="editStorySummary('${story.id}')" title="Editar resumen">
                            <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                <path fill="currentcolor" fill-rule="evenodd" d="M11.586.854a2 2 0 0 1 2.828 0l.732.732a2 2 0 0 1 0 2.828L10.01 9.551a2 2 0 0 1-.864.51l-3.189.91a.75.75 0 0 1-.927-.927l.91-3.189a2 2 0 0 1 .51-.864zm1.768 1.06a.5.5 0 0 0-.708 0l-.585.586L13.5 3.94l.586-.586a.5.5 0 0 0 0-.708zM12.439 5 11 3.56 7.51 7.052a.5.5 0 0 0-.128.216l-.54 1.891 1.89-.54a.5.5 0 0 0 .217-.127zM3 2.501a.5.5 0 0 0-.5.5v10a.5.5 0 0 0 .5.5h10a.5.5 0 0 0 .5-.5V10H15v3.001a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2v-10a2 2 0 0 1 2-2h3v1.5z" clip-rule="evenodd"></path>
                            </svg>
                        </button>
                    </div>
                </div>
            </div>
            <div class="story-right">
                <div class="story-status-dropdown-container">
                    <button class="story-status-badge" onclick="toggleStoryStatusMenu(event, '${story.id}')">
                        ${statusLabel}
                        <i class="fas fa-chevron-down" style="margin-left: 6px; font-size: 10px;"></i>
                    </button>
                    <div class="story-status-menu" id="story-status-menu-${story.id}">
                        <div class="story-status-option" onclick="changeStoryStatus('${story.id}', 'Backlog')">
                            <span class="status-dot" style="background: #9CA3AF;"></span>
                            TAREAS POR HACER
                        </div>
                        <div class="story-status-option" onclick="changeStoryStatus('${story.id}', 'InProgress')">
                            <span class="status-dot" style="background: #3B82F6;"></span>
                            EN CURSO
                        </div>
                        <div class="story-status-option" onclick="changeStoryStatus('${story.id}', 'Done')">
                            <span class="status-dot" style="background: #10B981;"></span>
                            FINALIZADA
                        </div>
                    </div>
                </div>
                <div class="story-points-inline">${story.storyPoints || '-'}</div>
                ${assignee ? `
                    <div class="assignee-avatar" style="background: ${assigneeColor}; color: white;" title="${assignee.name} (${assignee.role})">
                        ${assigneeInitials}
                    </div>
                ` : '<div class="assignee-avatar unassigned" title="Sin asignar">?</div>'}
                <div class="story-actions-dropdown-container">
                    <button class="story-actions-btn" onclick="toggleStoryActionsMenu(event, '${story.id}')">
                        <i class="fas fa-ellipsis-h"></i>
                    </button>
                    <div class="story-actions-menu" id="story-actions-menu-${story.id}">
                        <div class="story-action-option delete" onclick="deleteBacklogStory('${story.id}')">
                            <i class="fas fa-trash"></i>
                            Eliminar historia
                        </div>
                    </div>
                </div>
            </div>
        </div>
    `;
}

function dragBacklogStory(event, storyId) {
    event.dataTransfer.setData('storyId', storyId);
    event.dataTransfer.effectAllowed = 'move';
}

function allowBacklogDrop(event) {
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
}

async function dropStoryOnSprint(event, sprintId) {
    event.preventDefault();
    const storyId = event.dataTransfer.getData('storyId');
    const sprintSection = document.getElementById(sprintId);
    const actualSprintId = sprintSection?.dataset.sprintId;

    if (!storyId || !actualSprintId) return;

    try {
        await apiRequest(`/api/stories/${storyId}/move-to-sprint?sprintId=${encodeURIComponent(actualSprintId)}`, {
            method: 'POST'
        });
        showToast('Historia agregada al sprint', 'success');
        await loadBacklog();
    } catch (error) {
        console.error('Error moving story to sprint:', error);
        showToast('Error al agregar al sprint: ' + error.message, 'error');
    }
}

async function dropStoryOnBacklog(event) {
    event.preventDefault();
    const storyId = event.dataTransfer.getData('storyId');
    if (!storyId) return;

    try {
        await apiRequest(`/api/stories/${storyId}/move-to-backlog`, {
            method: 'POST'
        });
        showToast('Historia devuelta al backlog', 'success');
        await loadBacklog();
    } catch (error) {
        console.error('Error moving story to backlog:', error);
        showToast('Error al mover al backlog: ' + error.message, 'error');
    }
}

function getStoryTypeLabel(type) {
    const labels = {
        'story': 'Historia',
        'bug': 'Bug',
        'task': 'Tarea',
        'epic': 'Épica'
    };
    return labels[type] || 'Historia';
}

function toggleStoryStatusMenu(event, storyId) {
    event.stopPropagation();
    const menu = document.getElementById(`story-status-menu-${storyId}`);
    const allMenus = document.querySelectorAll('.story-status-menu');
    
    // Close all other menus
    allMenus.forEach(m => {
        if (m !== menu) m.style.display = 'none';
    });
    
    // Toggle current menu
    menu.style.display = menu.style.display === 'block' ? 'none' : 'block';
}

async function changeStoryStatus(storyId, newStatus) {
    // Close the menu
    const menu = document.getElementById(`story-status-menu-${storyId}`);
    if (menu) menu.style.display = 'none';
    
    // Update the story status in the UI
    const statusLabels = {
        'Backlog': 'TAREAS POR HACER',
        'InProgress': 'EN CURSO',
        'Done': 'FINALIZADA',
        'SprintBacklog': 'SPRINT BACKLOG'
    };
    
    try {
        await apiRequest(`/api/stories/${storyId}/status`, {
            method: 'PUT',
            body: JSON.stringify({ status: newStatus })
        });

        showToast(`Estado actualizado a: ${statusLabels[newStatus]}`, 'success');
        await loadBacklog();
    } catch (error) {
        console.error('Error updating story status:', error);
        showToast('Error al actualizar estado', 'error');
    }
}

// Close status menus when clicking outside
document.addEventListener('click', function(event) {
    if (!event.target.closest('.story-status-dropdown-container')) {
        document.querySelectorAll('.story-status-menu').forEach(menu => {
            menu.style.display = 'none';
        });
    }
    if (!event.target.closest('.story-actions-dropdown-container')) {
        document.querySelectorAll('.story-actions-menu').forEach(menu => {
            menu.style.display = 'none';
        });
    }
});

function toggleStoryActionsMenu(event, storyId) {
    event.stopPropagation();
    const menu = document.getElementById(`story-actions-menu-${storyId}`);
    const allMenus = document.querySelectorAll('.story-actions-menu');
    
    // Close all other menus
    allMenus.forEach(m => {
        if (m !== menu) m.style.display = 'none';
    });
    
    // Toggle current menu
    menu.style.display = menu.style.display === 'block' ? 'none' : 'block';
}

async function deleteBacklogStory(storyId) {
    console.log('=== deleteBacklogStory START ===', storyId);
    
    // Close the menu
    const menu = document.getElementById(`story-actions-menu-${storyId}`);
    if (menu) menu.style.display = 'none';
    
    // Show confirmation modal
    storyToDelete = storyId;
    showModal('delete-story-modal');
}

async function editStorySummary(storyId) {
    const card = document.querySelector(`[data-story-id="${storyId}"]`);
    if (!card) return;
    
    const titleSpan = card.querySelector('.story-title');
    if (!titleSpan) return;
    
    const currentTitle = titleSpan.textContent;
    const newTitle = prompt('Editar resumen de la historia:', currentTitle);
    
    if (newTitle !== null && newTitle.trim() !== '') {
        try {
            const existingStory = await apiRequest(`/api/stories/${storyId}`);
            await apiRequest(`/api/stories/${storyId}`, {
                method: 'PUT',
                body: JSON.stringify({
                    projectId: existingStory.projectId,
                    sprintId: existingStory.sprintId,
                    title: newTitle.trim(),
                    description: existingStory.description || '',
                    acceptanceCriteria: existingStory.acceptanceCriteria || '',
                    storyPoints: existingStory.storyPoints || 0,
                    priority: existingStory.priority || 2,
                    assigneeId: existingStory.assigneeId,
                    status: existingStory.status
                })
            });

            showToast('Resumen actualizado', 'success');
            await loadBacklog();
        } catch (error) {
            console.error('Error updating story title:', error);
            showToast('Error al actualizar historia', 'error');
        }
    }
}

function toggleStoryMenu(event, storyId) {
    event.stopPropagation();
    // Implementar menú desplegable si es necesario
    showToast('Menú de historia - Funcionalidad próximamente', 'info');
}

function toggleSprintSection(button) {
    const sprintContent = document.getElementById('sprint-content');
    const isExpanded = button.getAttribute('aria-expanded') === 'true';
    
    if (isExpanded) {
        sprintContent.style.display = 'none';
        button.setAttribute('aria-expanded', 'false');
        button.querySelector('svg').style.transform = 'rotate(-90deg)';
        button.setAttribute('title', 'Expandir');
    } else {
        sprintContent.style.display = 'block';
        button.setAttribute('aria-expanded', 'true');
        button.querySelector('svg').style.transform = 'rotate(0deg)';
        button.setAttribute('title', 'Contraer');
    }
}

function toggleBacklogSection(button) {
    const backlogContent = document.getElementById('backlog-content');
    const isExpanded = button.getAttribute('aria-expanded') === 'true';
    
    if (isExpanded) {
        backlogContent.style.display = 'none';
        button.setAttribute('aria-expanded', 'false');
        button.querySelector('svg').style.transform = 'rotate(-90deg)';
        button.setAttribute('title', 'Expandir');
    } else {
        backlogContent.style.display = 'block';
        button.setAttribute('aria-expanded', 'true');
        button.querySelector('svg').style.transform = 'rotate(0deg)';
        button.setAttribute('title', 'Contraer');
    }
}

function showCreateStoryFormBacklog() {
    const form = document.getElementById('backlog-create-story-form');
    const trigger = document.getElementById('backlog-create-story-trigger');
    const input = document.getElementById('backlog-new-story-title');
    const submitBtn = form.querySelector('.create-submit-btn');
    
    if (form && trigger) {
        form.style.display = 'block';
        trigger.style.display = 'none';
        input.focus();
        
        // Enable/disable submit button based on input
        input.addEventListener('input', function() {
            submitBtn.disabled = !this.value.trim();
        });
        
        // Handle Enter key
        input.addEventListener('keydown', function(e) {
            if (e.key === 'Enter' && this.value.trim()) {
                createBacklogStory();
            }
            if (e.key === 'Escape') {
                hideCreateStoryFormBacklog();
            }
        });
        
        // Handle submit button click
        submitBtn.onclick = function() {
            if (input.value.trim()) {
                createBacklogStory();
            }
        };
    }
}

function hideCreateStoryFormBacklog() {
    const form = document.getElementById('backlog-create-story-form');
    const trigger = document.getElementById('backlog-create-story-trigger');
    const input = document.getElementById('backlog-new-story-title');
    
    if (form && trigger) {
        form.style.display = 'none';
        trigger.style.display = 'block';
        input.value = '';
    }
}

async function createBacklogStory() {
    const titleInput = document.getElementById('backlog-new-story-title');
    const title = titleInput.value.trim();
    
    if (!title) {
        showToast('Por favor ingresa un título para la historia', 'error');
        return;
    }
    
    try {
        // 1. Crear la historia en el backend (sin sprint = backlog)
        const story = await apiRequest('/api/stories', {
            method: 'POST',
            body: JSON.stringify({
                projectId: selectedProjectId,
                sprintId: null,
                title,
                description: '',
                acceptanceCriteria: '',
                storyPoints: 0,
                priority: 2,
                status: 'Backlog'
            })
        });

        // 2. Agregar la historia dinámicamente al contenido del backlog (sin recargar)
        const backlogContent = document.getElementById('backlog-content');
        if (backlogContent) {
            const project = projects.find(p => p.id === selectedProjectId);
            const projectKey = project ? (project.key || project.name.substring(0, 4).toUpperCase()) : 'PROJ';
            
            // Crear tarjeta de historia
            const storyCard = createBacklogStoryCard(story, project?.members || [], projectKey);
            
            // Agregar al contenido del backlog
            if (backlogContent.innerHTML.trim() === '<!-- Backlog stories will be rendered here -->' || 
                backlogContent.innerHTML.trim() === '') {
                backlogContent.innerHTML = storyCard;
            } else {
                backlogContent.innerHTML += storyCard;
            }
            
            // Actualizar contador de actividades en el título del backlog
            const activityCount = backlogContent.querySelectorAll('.backlog-story-card').length;
            const backlogSection = document.querySelector('.backlog-section');
            const countSpan = backlogSection?.querySelector('.activity-count');
            if (countSpan) {
                countSpan.textContent = `(${activityCount} actividades)`;
            }
            
            // Actualizar badges del backlog
            const todoBadge = backlogSection?.querySelector('.backlog-badge.todo');
            if (todoBadge) {
                const currentCount = parseInt(todoBadge.textContent) || 0;
                todoBadge.textContent = currentCount + 1;
            }
        }

        showToast('Historia creada en backlog: ' + title, 'success');
        hideCreateStoryFormBacklog();
        await loadBacklog();
        
    } catch (error) {
        console.error('Error creating backlog story:', error);
        showToast('Error al crear historia: ' + error.message, 'error');
    }
}

async function createSprintFromBacklog() {
    // Generar nombre y fechas por defecto
    let sprintName;
    let sprintNumber;
    try {
        const existingSprints = await apiRequest(`/api/sprints/project/${selectedProjectId}`);
        sprintNumber = existingSprints.length + 1;
        sprintName = `Sprint ${sprintNumber}`;
    } catch (error) {
        console.error('Error obteniendo sprints existentes:', error);
        sprintNumber = nextSprintNumber++;
        sprintName = `Sprint ${sprintNumber}`;
    }

    // Calcular fechas por defecto (2 semanas)
    const today = new Date();
    const endDate = new Date(today);
    endDate.setDate(endDate.getDate() + 14);
    
    const startDateStr = today.toISOString().split('T')[0];
    const endDateStr = endDate.toISOString().split('T')[0];

    try {
        showToast(`Creando ${sprintName}...`, 'info');
        
        // 1. Crear sprint en backend directamente
        const sprint = await apiRequest('/api/sprints', {
            method: 'POST',
            body: {
                projectId: selectedProjectId,
                name: sprintName,
                goal: '',
                startDate: startDateStr,
                endDate: endDateStr,
                durationWeeks: 2
            }
        });

        // 2. Crear sección HTML dinámica del sprint (igual al Sprint 1, vacío)
        const sprintElement = createDynamicSprintSection(sprint, 0);
        
        if (sprintElement) {
            showToast(`${sprintName} creado exitosamente`, 'success');
            await loadBacklog();
        } else {
            showToast(`${sprintName} creado pero no se pudo mostrar`, 'warning');
        }
    } catch (error) {
        console.error('Error creating sprint:', error);
        showToast('Error al crear sprint: ' + error.message, 'error');
    }
}

function toggleDynamicSprintSection(button, sprintId) {
    const sprintContent = document.getElementById(`sprint-content-${sprintId}`);
    const isExpanded = button.getAttribute('aria-expanded') === 'true';
    
    if (isExpanded) {
        sprintContent.style.display = 'none';
        button.setAttribute('aria-expanded', 'false');
        button.querySelector('svg').style.transform = 'rotate(-90deg)';
        button.setAttribute('title', 'Expandir');
    } else {
        sprintContent.style.display = 'block';
        button.setAttribute('aria-expanded', 'true');
        button.querySelector('svg').style.transform = 'rotate(0deg)';
        button.setAttribute('title', 'Contraer');
    }
}

function showCreateStoryFormForSprint(sprintId) {
    const form = document.getElementById(`create-story-form-${sprintId}`);
    const trigger = document.getElementById(`create-story-trigger-${sprintId}`);
    const input = document.getElementById(`new-story-title-${sprintId}`);
    const submitBtn = form.querySelector('.create-submit-btn');
    
    if (form && trigger) {
        form.style.display = 'block';
        trigger.style.display = 'none';
        input.focus();
        
        // Enable/disable submit button based on input
        input.addEventListener('input', function() {
            submitBtn.disabled = !this.value.trim();
        });
        
        // Handle Enter key
        input.addEventListener('keydown', function(e) {
            if (e.key === 'Enter' && this.value.trim()) {
                createStoryForSprint(sprintId);
            }
            if (e.key === 'Escape') {
                hideCreateStoryFormForSprint(sprintId);
            }
        });
        
        // Handle submit button click
        submitBtn.onclick = function() {
            if (input.value.trim()) {
                createStoryForSprint(sprintId);
            }
        };
    }
}

function hideCreateStoryFormForSprint(sprintId) {
    const form = document.getElementById(`create-story-form-${sprintId}`);
    const trigger = document.getElementById(`create-story-trigger-${sprintId}`);
    const input = document.getElementById(`new-story-title-${sprintId}`);
    
    if (form && trigger) {
        form.style.display = 'none';
        trigger.style.display = 'block';
        input.value = '';
    }
}

async function createStoryForSprint(sprintId) {
    // For Sprint 1, the ID is different (no suffix)
    const inputId = sprintId === 'sprint-1' ? 'new-story-title' : `new-story-title-${sprintId}`;
    const titleInput = document.getElementById(inputId);
    const title = titleInput ? titleInput.value.trim() : '';
    
    if (!title) {
        showToast('Por favor ingresa un título para la historia', 'error');
        return;
    }
    
    // Obtener el ID real del sprint desde el dataset
    const sprintElement = document.getElementById(sprintId);
    const actualSprintId = sprintElement ? sprintElement.dataset.sprintId : null;
    
    if (!actualSprintId) {
        showToast('Error: No se encontró el ID del sprint', 'error');
        return;
    }
    
    try {
        // 1. Crear la historia en el backend asociada al sprint
        const story = await apiRequest('/api/stories', {
            method: 'POST',
            body: JSON.stringify({
                projectId: selectedProjectId,
                sprintId: actualSprintId,
                title,
                description: '',
                acceptanceCriteria: '',
                storyPoints: 0,
                priority: 2,
                status: 'Backlog'
            })
        });

        // 2. Agregar la historia dinámicamente al contenido del sprint (sin recargar)
        const sprintContent = document.getElementById(`sprint-content-${sprintId}`);
        if (sprintContent) {
            const project = projects.find(p => p.id === selectedProjectId);
            const projectKey = project ? (project.key || project.name.substring(0, 4).toUpperCase()) : 'PROJ';
            
            // Crear tarjeta de historia
            const storyCard = createBacklogStoryCard(story, project?.members || [], projectKey);
            
            // Agregar al contenido del sprint
            if (sprintContent.innerHTML.trim() === '<!-- Historias se cargarán aquí -->') {
                sprintContent.innerHTML = storyCard;
            } else {
                sprintContent.innerHTML += storyCard;
            }
            
            // Actualizar contador de actividades en el título
            const activityCount = sprintContent.querySelectorAll('.backlog-story-card').length;
            const sprintSection = document.getElementById(sprintId);
            const countSpan = sprintSection?.querySelector('.activity-count');
            if (countSpan) {
                countSpan.textContent = `(${activityCount} actividades)`;
            }
            
            // Actualizar badge de pendientes
            const todoBadge = sprintSection?.querySelector('.backlog-badge.todo');
            if (todoBadge) {
                todoBadge.textContent = activityCount;
            }
        }

        showToast('Historia creada: ' + title, 'success');
        hideCreateStoryFormForSprint(sprintId);
        await loadBacklog();
        
        // 3. Habilitar botón "Iniciar sprint" si hay historias
        const sprintSection = document.getElementById(sprintId);
        const startSprintBtn = sprintSection?.querySelector('.create-sprint-btn');
        if (startSprintBtn && startSprintBtn.disabled) {
            startSprintBtn.disabled = false;
        }
        
    } catch (error) {
        console.error('Error creating sprint story:', error);
        showToast('Error al crear historia: ' + error.message, 'error');
    }
}

function toggleSprintOptionsMenu(event, sprintId) {
    event.stopPropagation();
    console.log('toggleSprintOptionsMenu called with:', sprintId);
    
    const menu = document.getElementById(`sprint-options-menu-${sprintId}`);
    console.log('Menu element found:', menu);
    
    if (!menu) {
        console.error('Menu not found for sprint:', sprintId);
        // Try to find it with querySelector
        const altMenu = document.querySelector(`[id^="sprint-options-menu-"]`);
        console.log('Alternative menu found:', altMenu);
        return;
    }
    
    // Close all other sprint option menus
    document.querySelectorAll('.sprint-options-menu').forEach(m => {
        if (m !== menu) m.style.display = 'none';
    });
    
    const isVisible = menu.style.display === 'block';
    menu.style.display = isVisible ? 'none' : 'block';
    console.log('Menu visibility toggled to:', menu.style.display);
}

function editSprint(sprintId) {
    // Close the menu
    const menu = document.getElementById(`sprint-options-menu-${sprintId}`);
    if (menu) menu.style.display = 'none';
    
    // Get sprint element - try by ID first
    let sprintSection = document.getElementById(sprintId);
    
    // If not found by ID, try querySelector
    if (!sprintSection) {
        sprintSection = document.querySelector(`[id="${sprintId}"]`);
    }
    
    if (!sprintSection) {
        showToast('Error: No se encontró el sprint', 'error');
        console.error('Sprint not found for editing:', sprintId);
        return;
    }
    
    const titleSpan = sprintSection.querySelector('.backlog-section-title');
    if (!titleSpan) {
        showToast('Error: No se encontró el título del sprint', 'error');
        return;
    }
    
    // Extract current sprint name (remove the activity count part)
    const currentText = titleSpan.childNodes[0].textContent.trim();
    const newName = prompt('Editar nombre del sprint:', currentText);
    
    if (newName !== null && newName.trim() !== '') {
        // Update the title while preserving the activity count
        const activityCount = titleSpan.querySelector('.activity-count');
        titleSpan.innerHTML = `${newName} ${activityCount ? activityCount.outerHTML : ''}`;
        showToast('Sprint actualizado', 'success');
    }
}

async function handleDeleteSprintClick(event, sprintId) {
    event.stopPropagation();
    console.log('=== handleDeleteSprintClick START ===');
    console.log('Sprint ID:', sprintId);
    
    // Close the menu
    const menu = document.getElementById(`sprint-options-menu-${sprintId}`);
    console.log('Menu found:', menu);
    if (menu) menu.style.display = 'none';
    
    // Confirm deletion
    if (!confirm('¿Estás seguro de que deseas eliminar este sprint? Las historias se moverán al backlog.')) {
        console.log('Deletion cancelled by user');
        return;
    }
    
    // Get sprint section
    let sprintSection = document.getElementById(sprintId);
    if (!sprintSection) {
        sprintSection = document.querySelector(`[id="${sprintId}"]`);
    }
    
    if (!sprintSection) {
        showToast('Error: No se encontró el sprint', 'error');
        console.error('Sprint not found:', sprintId);
        return;
    }
    
    // Get the actual sprint ID from the dataset
    const actualSprintId = sprintSection.dataset.sprintId;
    console.log('Actual sprint ID from dataset:', actualSprintId);
    
    try {
        // 1. Eliminar sprint del backend (si tiene ID real)
        if (actualSprintId && actualSprintId !== 'null' && actualSprintId !== 'undefined') {
            console.log('Deleting sprint from backend:', actualSprintId);
            await apiRequest(`/api/sprints/${actualSprintId}`, {
                method: 'DELETE'
            });
            console.log('Sprint deleted from backend successfully');
        } else {
            console.log('No actual sprint ID found, skipping backend deletion');
        }
        
        // 2. Mover historias al backlog en el frontend
        const sprintStories = sprintSection.querySelectorAll('.backlog-story-card');
        console.log('Stories found in sprint:', sprintStories.length);
        if (sprintStories.length > 0) {
            const backlogContent = document.getElementById('backlog-content');
            if (backlogContent) {
                sprintStories.forEach(story => {
                    const clonedStory = story.cloneNode(true);
                    backlogContent.appendChild(clonedStory);
                });
            }
        }
        
        // 3. Eliminar sección del sprint del DOM
        if (sprintSection.parentNode) {
            sprintSection.parentNode.removeChild(sprintSection);
            console.log('Sprint removed from DOM');
            showToast('Sprint eliminado permanentemente', 'success');
        }
        
        // 4. Actualizar contadores
        updateActivityCount();
        
    } catch (error) {
        console.error('Error deleting sprint:', error);
        showToast('Error al eliminar sprint: ' + error.message, 'error');
    }
    
    console.log('=== handleDeleteSprintClick END ===');
}

function deleteSprint(sprintId) {
    console.log('=== deleteSprint START ===');
    console.log('deleteSprint called with:', sprintId);
    
    // Close the menu
    const menu = document.getElementById(`sprint-options-menu-${sprintId}`);
    console.log('Menu found:', menu);
    if (menu) menu.style.display = 'none';
    
    // Confirm deletion
    if (!confirm('¿Estás seguro de que deseas eliminar este sprint? Las historias se moverán al backlog.')) {
        console.log('Deletion cancelled by user');
        return;
    }
    
    // Get sprint section - try by ID first
    let sprintSection = document.getElementById(sprintId);
    console.log('Found sprintSection by ID:', sprintSection);
    
    // If not found by ID, try to find by data attribute or search in container
    if (!sprintSection) {
        sprintSection = document.querySelector(`[id="${sprintId}"]`);
        console.log('Found sprintSection by querySelector:', sprintSection);
    }
    
    // Try to find sprint by searching all sprint sections
    if (!sprintSection) {
        const allSprints = document.querySelectorAll('.sprint-section');
        console.log('All sprint sections found:', allSprints.length);
        allSprints.forEach((sprint, index) => {
            console.log(`Sprint ${index}:`, sprint.id);
        });
    }
    
    if (!sprintSection) {
        showToast('Error: No se encontró el sprint', 'error');
        console.error('Sprint not found:', sprintId);
        return;
    }
    
    // Move stories to backlog if any exist
    const sprintStories = sprintSection.querySelectorAll('.backlog-story-card');
    console.log('Stories found in sprint:', sprintStories.length);
    if (sprintStories.length > 0) {
        const backlogContent = document.getElementById('backlog-content');
        if (backlogContent) {
            sprintStories.forEach(story => {
                // Clone and append to backlog
                const clonedStory = story.cloneNode(true);
                backlogContent.appendChild(clonedStory);
            });
        }
    }
    
    // Remove the sprint section using parentNode.removeChild for better compatibility
    console.log('Attempting to remove sprint section. Parent:', sprintSection.parentNode);
    console.log('Sprint section before removal:', sprintSection);
    console.log('Sprint section class:', sprintSection.className);
    
    if (sprintSection.parentNode) {
        sprintSection.parentNode.removeChild(sprintSection);
        console.log('Sprint removed successfully');
        showToast('Sprint eliminado. Las historias se movieron al backlog.', 'success');
        
        // Verify removal
        const stillExists = document.getElementById(sprintId);
        console.log('Sprint still exists after removal:', stillExists);
    } else {
        showToast('Error al eliminar el sprint', 'error');
        console.error('No parent node found for sprint:', sprintId);
    }
    
    // Update activity counts
    updateActivityCount();
}

// Variable global para almacenar el ID del sprint que se está iniciando
let currentSprintToStart = null;

function showStartSprintModal(activitiesCount, sprintName = '', sprintId = null) {
    console.log('showStartSprintModal called with:', { activitiesCount, sprintName, sprintId });
    
    // Guardar el ID del sprint si se proporciona (para sprints existentes)
    currentSprintToStart = sprintId;
    
    const modal = document.getElementById('start-sprint-modal');
    const countElement = document.getElementById('sprint-activities-count');
    const submitBtn = document.getElementById('start-sprint-submit-btn');
    
    if (modal && countElement) {
        countElement.textContent = `${activitiesCount} actividades se incluirán en este sprint.`;
        modal.classList.add('active');
        
        // Cambiar texto del botón según si es sprint nuevo o existente
        if (submitBtn) {
            submitBtn.textContent = sprintId ? 'Iniciar sprint' : 'Crear sprint';
        }
        
        // Set default start date to today
        const startDateInput = document.getElementById('sprint-start-date');
        if (startDateInput) {
            const today = new Date().toISOString().split('T')[0];
            startDateInput.value = today;
        }
        
        // Reset form only if no sprint name provided
        if (!sprintName) {
            document.getElementById('start-sprint-form').reset();
        }
        
        // Set sprint name if provided (do this last to avoid being overwritten)
        if (sprintName) {
            console.log('Setting sprint name to:', sprintName);
            
            // Delay to ensure modal is fully rendered
            setTimeout(() => {
                const sprintNameInput = document.getElementById('sprint-name');
                if (sprintNameInput) {
                    console.log('Found sprint name input, current value:', sprintNameInput.value);
                    
                    // Set value
                    sprintNameInput.value = sprintName;
                    
                    // Force white color with !important via style attribute
                    sprintNameInput.setAttribute('style', 'color: #ffffff !important; background-color: transparent;');
                    
                    console.log('After setting - value:', sprintNameInput.value, 'style:', sprintNameInput.getAttribute('style'));
                } else {
                    console.log('Sprint name input not found!');
                }
            }, 200);
        }
    }
}

// Función para abrir modal de sprint (obtiene conteo y nombre dinámicamente desde el DOM)
function showStartSprintModalForSprint(sprintId) {
    const sprintSection = document.getElementById(sprintId);
    if (!sprintSection) {
        console.error('Sprint section not found:', sprintId);
        return;
    }
    
    // Obtener el nombre del sprint desde el título
    const titleSpan = sprintSection.querySelector('.backlog-section-title');
    const sprintName = titleSpan ? titleSpan.textContent.replace(/\s*\(\d+ actividades?\)/, '').trim() : sprintId;
    
    // Obtener el ID real del sprint desde dataset
    const actualSprintId = sprintSection.dataset.sprintId;
    
    // Contar actividades dinámicamente desde el DOM
    // Sprint 1 usa 'sprint-content' sin sufijo, los demás usan 'sprint-content-{sprintId}'
    const contentId = sprintId === 'sprint-1' ? 'sprint-content' : `sprint-content-${sprintId}`;
    const sprintContent = document.getElementById(contentId);
    const storyCards = sprintContent ? sprintContent.querySelectorAll('.backlog-story-card') : [];
    const activityCount = storyCards.length;
    
    console.log('Opening sprint modal for:', { sprintId, sprintName, actualSprintId, activityCount, contentId });
    
    // Llamar al modal con los valores correctos
    showStartSprintModal(activityCount, sprintName, actualSprintId);
}

// Variables globales para completar sprint
let currentSprintToComplete = null;
let currentSprintStories = [];

// Función para completar un sprint activo
async function completeSprint(sprintId) {
    const sprint = sprints.find(s => s.id === sprintId);
    if (!sprint) {
        showToast('Sprint no encontrado', 'error');
        return;
    }
    
    // Obtener historias del sprint
    const sprintStories = stories.filter(s => s.sprintId === sprintId);
    currentSprintStories = sprintStories;
    currentSprintToComplete = sprintId;
    
    // Contar actividades completadas vs abiertas
    const completedCount = sprintStories.filter(s => s.status === 'Done').length;
    const openCount = sprintStories.length - completedCount;
    
    // Llenar modal con datos
    document.getElementById('complete-sprint-name').textContent = sprint.name || 'Sprint';
    document.getElementById('complete-sprint-completed').textContent = completedCount;
    document.getElementById('complete-sprint-open').textContent = openCount;
    
    // Llenar opciones de destino (otros sprints activos o backlog)
    const destinationSelect = document.getElementById('complete-sprint-destination');
    destinationSelect.innerHTML = '<option value="backlog">Backlog</option>';
    
    // Agregar otros sprints que no estén completados
    sprints.filter(s => s.id !== sprintId && s.status !== 'Completed').forEach(s => {
        const option = document.createElement('option');
        option.value = s.id;
        option.textContent = s.name;
        destinationSelect.appendChild(option);
    });
    
    showModal('complete-sprint-modal');
}

// Función para confirmar completar sprint
async function confirmCompleteSprint() {
    if (!currentSprintToComplete) {
        showToast('Error: No hay sprint seleccionado', 'error');
        return;
    }
    
    const destination = document.getElementById('complete-sprint-destination').value;
    const destinationName = destination === 'backlog' ? 'Backlog' : 
        sprints.find(s => s.id === destination)?.name || 'Otro sprint';
    
    try {
        // Mover historias abiertas si es necesario
        const openStories = currentSprintStories.filter(s => s.status !== 'Done');
        
        if (openStories.length > 0 && destination !== 'backlog') {
            // Mover historias al sprint seleccionado
            for (const story of openStories) {
                await apiRequest(`/api/stories/${story.id}/move`, {
                    method: 'POST',
                    body: JSON.stringify({ sprintId: destination, status: 'SprintBacklog' })
                });
            }
        }
        
        // Completar el sprint
        await apiRequest(`/api/sprints/${currentSprintToComplete}/complete`, {
            method: 'POST'
        });
        
        showToast(`Sprint completado exitosamente. ${openStories.length > 0 ? `Actividades abiertas movidas a ${destinationName}.` : ''}`, 'success');
        hideModal('complete-sprint-modal');
        loadBacklog(); // Recargar la vista
        
        // Limpiar variables
        currentSprintToComplete = null;
        currentSprintStories = [];
    } catch (error) {
        console.error('Error completing sprint:', error);
        showToast('Error al completar el sprint', 'error');
    }
}

function calculateEndDate() {
    const duration = document.getElementById('sprint-duration').value;
    const startDate = document.getElementById('sprint-start-date').value;
    const endDateInput = document.getElementById('sprint-end-date');
    
    if (duration && startDate && endDateInput) {
        const start = new Date(startDate);
        const weeks = parseInt(duration);
        const end = new Date(start);
        end.setDate(end.getDate() + (weeks * 7));
        
        endDateInput.value = end.toISOString().split('T')[0];
    }
}

async function submitStartSprint() {
    const name = document.getElementById('sprint-name').value.trim();
    const duration = document.getElementById('sprint-duration').value;
    const startDate = document.getElementById('sprint-start-date').value;
    const endDate = document.getElementById('sprint-end-date').value;
    const goal = document.getElementById('sprint-goal').value.trim();
    
    if (!name || !duration || !startDate || !endDate) {
        showToast('Por favor completa todos los campos obligatorios', 'error');
        return;
    }
    
    try {
        // CASO 1: Sprint existente (cuando currentSprintToStart tiene valor)
        if (currentSprintToStart) {
            console.log('Iniciando sprint existente:', currentSprintToStart);
            
            // 1. Actualizar sprint existente con status "Active"
            await apiRequest(`/api/sprints/${currentSprintToStart}`, {
                method: 'PUT',
                body: {
                    projectId: selectedProjectId,
                    name,
                    goal,
                    startDate,
                    endDate,
                    durationWeeks: parseInt(duration),
                    status: 'Active'
                }
            });
            
            showToast(`Sprint "${name}" iniciado exitosamente`, 'success');
            hideModal('start-sprint-modal');
            
            // 2. Limpiar variable global
            currentSprintToStart = null;
            
            // 3. Redirigir al tablero para ver las historias
            switchProjectTab('board');
            return;
        }
        
        // CASO 2: Crear nuevo sprint (cuando currentSprintToStart es null)
        console.log('Creando nuevo sprint');
        
        // 1. Crear sprint en backend con status "Active"
        const sprint = await apiRequest('/api/sprints', {
            method: 'POST',
            body: {
                projectId: selectedProjectId,
                name,
                goal,
                startDate,
                endDate,
                durationWeeks: parseInt(duration),
                status: 'Active'
            }
        });

        // 2. Mover historias al sprint (usando seleccionadas o todas las del backlog)
        const storiesToMove = window.selectedStoriesForSprint || [];
        let movedStoryCount = 0;
        
        if (storiesToMove.length > 0) {
            // Mover historias seleccionadas
            for (const storyId of storiesToMove) {
                try {
                    await apiRequest(`/api/stories/${storyId}/move-to-sprint?sprintId=${encodeURIComponent(sprint.id)}`, {
                        method: 'POST'
                    });
                    movedStoryCount++;
                } catch (moveError) {
                    console.error(`Error moviendo historia ${storyId}:`, moveError);
                }
            }
        } else {
            // Fallback: mover todas las historias del backlog
            const backlogStories = await apiRequest(`/api/stories/project/${selectedProjectId}/backlog`);
            for (const story of backlogStories) {
                try {
                    await apiRequest(`/api/stories/${story.id}/move-to-sprint?sprintId=${encodeURIComponent(sprint.id)}`, {
                        method: 'POST'
                    });
                    movedStoryCount++;
                } catch (moveError) {
                    console.error(`Error moviendo historia ${story.id}:`, moveError);
                }
            }
        }

        // 3. Crear sección HTML dinámica del sprint
        createDynamicSprintSection(sprint, movedStoryCount);

        showToast(`Sprint "${name}" creado y iniciado`, 'success');
        hideModal('start-sprint-modal');
        
        // 4. Limpiar selección
        window.selectedStoriesForSprint = null;
        
        // 5. Redirigir al tablero para ver las historias
        switchProjectTab('board');
    } catch (error) {
        console.error('Error al iniciar/crear sprint:', error);
        showToast('Error al iniciar sprint: ' + error.message, 'error');
    }
}

// Función para crear sección HTML dinámica del sprint
function createDynamicSprintSection(sprint, storyCount) {
    const backlogContainer = document.querySelector('.backlog-container');
    if (!backlogContainer) return;
    
    // Generar ID único para el sprint
    const sprintId = `sprint-${sprint.id || Date.now()}`;
    const sprintNumber = sprint.name.replace(/\D/g, '') || nextSprintNumber++;
    
    // Crear sección del sprint
    const sprintSection = document.createElement('div');
    sprintSection.className = 'sprint-section';
    sprintSection.setAttribute('data-drop-target-for-element', 'true');
    sprintSection.id = sprintId;
    sprintSection.dataset.sprintId = sprint.id;
    
    sprintSection.innerHTML = `
        <div class="backlog-section-header">
            <div class="backlog-header-left">
                <label class="backlog-checkbox-label">
                    <input type="checkbox" class="backlog-checkbox" aria-label="Seleccionar todas las actividades en sprint">
                    <svg width="20" height="20" viewBox="0 0 24 24" role="presentation">
                        <g fill-rule="evenodd">
                            <rect fill="currentColor" x="5.5" y="5.5" width="13" height="13" rx="1.5"></rect>
                        </g>
                    </svg>
                </label>
                <button class="backlog-collapse-btn" aria-expanded="true" onclick="toggleDynamicSprintSection(this, '${sprintId}')" title="Contraer">
                    <svg fill="none" viewBox="0 0 16 16" role="presentation" style="transform: rotate(0deg); transition: transform 0.2s;">
                        <path fill="currentcolor" d="m14.53 6.03-6 6a.75.75 0 0 1-1.004.052l-.056-.052-6-6 1.06-1.06L8 10.44l5.47-5.47z"></path>
                    </svg>
                </button>
                <span class="backlog-section-title">${sprint.name} <span class="activity-count">(${storyCount} actividades)</span></span>
            </div>
            <div class="backlog-header-right">
                <div class="backlog-badges">
                    <div class="backlog-badge todo" title="Pendiente">${storyCount}</div>
                    <div class="backlog-badge inprogress" title="En curso">0</div>
                    <div class="backlog-badge done" title="Hecho">0</div>
                </div>
                <button type="button" class="create-sprint-btn" onclick="showStartSprintModalForSprint('${sprintId}')" disabled>Iniciar sprint</button>
                <div class="sprint-menu-container">
                    <button type="button" class="sprint-menu-btn" onclick="toggleSprintOptionsMenu(event, '${sprintId}')" title="Más opciones">
                        <svg fill="none" viewBox="0 0 16 16" role="presentation">
                            <path fill="currentcolor" fill-rule="evenodd" d="M8 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4M6 8a1 1 0 1 1 2 0 1 1 0 0 1-2 0m5-2a2 2 0 1 1 0 4 2 2 0 0 1 0-4m0 1a1 1 0 1 0 0 2 1 1 0 0 0 0-2M4 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4m0 1a1 1 0 1 1 0 2 1 1 0 0 1 0-2" clip-rule="evenodd"></path>
                        </svg>
                    </button>
                    <div class="sprint-options-menu" id="sprint-options-menu-${sprintId}" style="display: none;" onclick="event.stopPropagation()">
                        <button class="sprint-option-item" onclick="event.stopPropagation(); editSprint('${sprintId}');">
                            <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                <path fill="currentcolor" fill-rule="evenodd" d="M11.586.854a2 2 0 0 1 2.828 0l.732.732a2 2 0 0 1 0 2.828L10.01 9.551a2 2 0 0 1-.864.51l-3.189.91a.75.75 0 0 1-.927-.927l.91-3.189a2 2 0 0 1 .51-.864zm1.768 1.06a.5.5 0 0 0-.708 0l-.585.586L13.5 3.94l.586-.586a.5.5 0 0 0 0-.708zM12.439 5 11 3.56 7.51 7.052a.5.5 0 0 0-.128.216l-.54 1.891 1.89-.54a.5.5 0 0 0 .217-.127zM3 2.501a.5.5 0 0 0-.5.5v10a.5.5 0 0 0 .5.5h10a.5.5 0 0 0 .5-.5V10H15v3.001a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2v-10a2 2 0 0 1 2-2h3v1.5z" clip-rule="evenodd"></path>
                            </svg>
                            Editar sprint
                        </button>
                        <button class="sprint-option-item delete" onclick="handleDeleteSprintClick(event, '${sprintId}')">
                            <svg fill="none" viewBox="0 0 16 16" role="presentation">
                                <path fill="currentcolor" fill-rule="evenodd" d="M10 2a1 1 0 0 1 1 1v1h2.5a.5.5 0 0 1 0 1h-.538l-.566 8.486A2.5 2.5 0 0 1 9.9 15.6H6.1a2.5 2.5 0 0 1-2.496-2.114L3.038 5H2.5a.5.5 0 0 1 0-1H5V3a1 1 0 0 1 1-1h4m-5 3h9l-.56 8.397a1.5 1.5 0 0 1-1.498 1.268H6.057a1.5 1.5 0 0 1-1.498-1.268zM9 3H7v1h2z" clip-rule="evenodd"></path>
                            </svg>
                            Eliminar sprint
                        </button>
                    </div>
                </div>
            </div>
        </div>
        <div class="sprint-content" id="sprint-content-${sprintId}" ondragover="allowBacklogDrop(event)" ondrop="dropStoryOnSprint(event, '${sprintId}')">
            <!-- Historias se cargarán aquí -->
        </div>
        <div class="backlog-nudge-container" id="create-story-container-${sprintId}">
            <div class="create-story-form" id="create-story-form-${sprintId}" style="display: none;">
                <div class="create-form-row">
                    <label class="story-checkbox-label">
                        <input type="checkbox" class="story-checkbox" aria-label="Seleccionar esta historia">
                        <svg width="24" height="24" viewBox="0 0 24 24" role="presentation">
                            <g fill-rule="evenodd">
                                <rect fill="currentColor" x="5.5" y="5.5" width="13" height="13" rx="1.5"></rect>
                            </g>
                        </svg>
                    </label>
                    <input type="text" class="story-title-input" aria-label="Work item summary" maxlength="255" placeholder="Describe qué hay que hacer." id="new-story-title-${sprintId}">
                    <button class="due-date-btn" aria-label="Fecha de vencimiento" type="button">
                        <svg fill="none" viewBox="0 0 16 16" role="presentation">
                            <path fill="currentcolor" fill-rule="evenodd" d="M4.5 2.5v2H6v-2h4v2h1.5v-2H13a.5.5 0 0 1 .5.5v3h-11V3a.5.5 0 0 1 .5-.5zm-2 5V13a.5.5 0 0 0 .5.5h10a.5.5 0 0 0 .5-.5V7.5zm9-6.5H13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V3a2 2 0 0 1 2-2h1.5V0H6v1h4V0h1.5z" clip-rule="evenodd"></path>
                        </svg>
                    </button>
                    <button class="assignee-btn" aria-label="Sin asignar" type="button">
                        <div class="assignee-avatar">
                            <svg fill="none" viewBox="-4 -4 24 24" role="presentation">
                                <path fill="currentcolor" fill-rule="evenodd" d="M8 1.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5M4 4a4 4 0 1 1 8 0 4 4 0 0 1-8 0m-2 9a3.75 3.75 0 0 1 3.75-3.75h4.5A3.75 3.75 0 0 1 14 13v2h-1.5v-2a2.25 2.25 0 0 0-2.25-2.25h-4.5A2.25 2.25 0 0 0 3.5 13v2H2z" clip-rule="evenodd"></path>
                            </svg>
                        </div>
                    </button>
                    <button class="create-submit-btn" type="button" disabled onclick="createStoryForSprint('${sprintId}')">
                        <span>Crear</span>
                        <svg fill="none" viewBox="0 0 16 16" role="presentation">
                            <path fill="currentcolor" fill-rule="evenodd" d="M12.5 8V3H14v5.438c0 .586-.476 1.062-1.062 1.062H4.56l2.72 2.72-1.061 1.06-4-4a.75.75 0 0 1 0-1.06l4-4 1.06 1.06L4.56 8z" clip-rule="evenodd"></path>
                        </svg>
                    </button>
                </div>
            </div>
            <div class="create-story-trigger" id="create-story-trigger-${sprintId}">
                <button class="create-story-btn" onclick="showCreateStoryFormForSprint('${sprintId}')">
                    <svg fill="none" viewBox="0 0 16 16" role="presentation">
                        <path fill="currentcolor" fill-rule="evenodd" d="M7.25 8.75V15h1.5V8.75H15v-1.5H8.75V1h-1.5v6.25H1v1.5z" clip-rule="evenodd"></path>
                    </svg>
                    Crear
                </button>
            </div>
        </div>
    `;
    
    // Insertar antes de la sección del backlog
    const backlogSection = backlogContainer.querySelector('.backlog-section');
    if (backlogSection) {
        backlogContainer.insertBefore(sprintSection, backlogSection);
    } else {
        backlogContainer.appendChild(sprintSection);
    }
    
    return sprintSection;
}

function toggleSprintMenu(event) {
    event.stopPropagation();
    const dropdown = document.getElementById('sprint-menu-dropdown');
    const isVisible = dropdown.style.display === 'block';
    
    // Close all other dropdowns
    document.querySelectorAll('.sprint-menu-dropdown').forEach(d => {
        if (d !== dropdown) d.style.display = 'none';
    });
    
    dropdown.style.display = isVisible ? 'none' : 'block';
}

function editSprint() {
    showToast('Editar sprint - Funcionalidad próximamente', 'info');
    closeSprintMenu();
}

function showCreateStoryForm() {
    const form = document.getElementById('create-story-form');
    const trigger = document.getElementById('create-story-trigger');
    
    form.style.display = 'block';
    trigger.style.display = 'none';
    
    // Focus on title input
    setTimeout(() => {
        document.getElementById('new-story-title').focus();
    }, 100);
}

function hideCreateStoryForm() {
    const form = document.getElementById('create-story-form');
    const trigger = document.getElementById('create-story-trigger');
    
    form.style.display = 'none';
    trigger.style.display = 'block';
    
    // Clear form
    document.getElementById('new-story-title').value = '';
    updateCreateButtonState();
}

function updateCreateButtonState() {
    const titleInput = document.getElementById('new-story-title');
    const submitBtn = document.querySelector('.create-submit-btn');
    
    if (titleInput.value.trim().length > 0) {
        submitBtn.disabled = false;
    } else {
        submitBtn.disabled = true;
    }
}

function updateStartSprintButtonState() {
    const sprintContent = document.getElementById('sprint-content');
    const startSprintBtn = document.querySelector('.sprint-section .create-sprint-btn');
    
    if (sprintContent && startSprintBtn) {
        const storyCount = sprintContent.querySelectorAll('.backlog-story-card').length;
        startSprintBtn.disabled = storyCount === 0;
        
        // Update activity count in title
        const activityCount = document.querySelector('.sprint-section .activity-count');
        if (activityCount) {
            activityCount.textContent = `(${storyCount} actividades)`;
        }
    }
}

async function createStory() {
    const titleInput = document.getElementById('new-story-title');
    const title = titleInput.value.trim();
    
    if (!title) {
        showToast('Por favor ingresa un título para la historia', 'error');
        return;
    }
    
    try {
        // 1. Crear la historia en el backend (Sprint 1 no tiene sprintId real, va al backlog)
        const story = await apiRequest('/api/stories', {
            method: 'POST',
            body: JSON.stringify({
                projectId: selectedProjectId,
                sprintId: null, // Sprint 1 es conceptual, las historias van al backlog
                title,
                description: '',
                acceptanceCriteria: '',
                storyPoints: 0,
                priority: 2,
                status: 'Backlog'
            })
        });

        // 2. Agregar la historia dinámicamente al Sprint 1 (visualmente)
        const sprintContent = document.getElementById('sprint-content');
        if (sprintContent) {
            const project = projects.find(p => p.id === selectedProjectId);
            const projectKey = project ? (project.key || project.name.substring(0, 4).toUpperCase()) : 'PROJ';
            
            // Crear tarjeta de historia
            const storyCard = createBacklogStoryCard(story, project?.members || [], projectKey);
            
            // Agregar al contenido del sprint
            if (sprintContent.innerHTML.trim() === '<!-- Stories will be rendered here -->' || 
                sprintContent.innerHTML.trim() === '') {
                sprintContent.innerHTML = storyCard;
            } else {
                sprintContent.innerHTML += storyCard;
            }
            
            // Actualizar contador de actividades en el título del Sprint 1
            const activityCount = sprintContent.querySelectorAll('.backlog-story-card').length;
            const sprintSection = document.getElementById('sprint-1')?.closest('.sprint-section');
            const countSpan = sprintSection?.querySelector('.activity-count');
            if (countSpan) {
                countSpan.textContent = `(${activityCount} actividades)`;
            }
            
            // Actualizar badge de pendientes
            const todoBadge = sprintSection?.querySelector('.backlog-badge.todo');
            if (todoBadge) {
                todoBadge.textContent = activityCount;
            }
            
            // Habilitar botón "Iniciar sprint"
            const startSprintBtn = sprintSection?.querySelector('.create-sprint-btn');
            if (startSprintBtn) {
                startSprintBtn.disabled = false;
            }
        }

        showToast('Historia creada: ' + title, 'success');
        hideCreateStoryForm();
        
    } catch (error) {
        console.error('Error creating story:', error);
        showToast('Error al crear historia: ' + error.message, 'error');
    }
}

function updateActivityCount() {
    // Update all sprint sections
    document.querySelectorAll('.sprint-section').forEach(sprintSection => {
        const sprintContent = sprintSection.querySelector('.sprint-content, [id^="sprint-content-"]');
        const activityCountElement = sprintSection.querySelector('.activity-count');
        const startSprintBtn = sprintSection.querySelector('.create-sprint-btn');
        
        if (sprintContent && activityCountElement) {
            const storyCards = sprintContent.querySelectorAll('.backlog-story-card');
            const count = storyCards.length;
            
            // Update activity count text
            activityCountElement.textContent = `(${count} actividades)`;
            
            // Update start sprint button state
            if (startSprintBtn) {
                startSprintBtn.disabled = count === 0;
            }
        }
    });
}

// Add event listeners when create form is shown
function setupCreateFormListeners() {
    const titleInput = document.getElementById('new-story-title');
    const createBtn = document.querySelector('.create-submit-btn');
    
    if (titleInput) {
        titleInput.addEventListener('input', updateCreateButtonState);
        titleInput.addEventListener('keydown', function(e) {
            if (e.key === 'Escape') {
                hideCreateStoryForm();
            } else if (e.key === 'Enter' && e.ctrlKey) {
                createStory();
            }
        });
    }
    
    if (createBtn) {
        createBtn.addEventListener('click', createStory);
    }
}

// Update showCreateStoryForm to setup listeners
function showCreateStoryForm() {
    const form = document.getElementById('create-story-form');
    const trigger = document.getElementById('create-story-trigger');
    
    form.style.display = 'block';
    trigger.style.display = 'none';
    
    // Focus on title input
    setTimeout(() => {
        document.getElementById('new-story-title').focus();
        setupCreateFormListeners();
    }, 100);
}

function deleteSprint() {
    if (confirm('¿Estás seguro de que deseas eliminar este sprint?')) {
        showToast('Sprint eliminado', 'success');
        closeSprintMenu();
    }
}

function closeSprintMenu() {
    const dropdown = document.getElementById('sprint-menu-dropdown');
    if (dropdown) dropdown.style.display = 'none';
}

// Close dropdown when clicking outside
document.addEventListener('click', function(event) {
    if (!event.target.closest('.sprint-menu-btn')) {
        closeSprintMenu();
    }
});

// Close create story form when clicking outside (only for dynamic sprints, NOT backlog)
document.addEventListener('click', function(event) {
    // Check all create story forms except Sprint 1 and backlog
    document.querySelectorAll('.create-story-form').forEach(form => {
        // Skip backlog form and Sprint 1 form
        if (form.id === 'backlog-create-story-form' || form.id === 'create-story-form') {
            return;
        }
        
        if (form.style.display === 'block') {
            // Get the corresponding trigger for dynamic sprint
            const formId = form.id;
            const sprintId = formId.replace('create-story-form-', '');
            const triggerId = `create-story-trigger-${sprintId}`;
            const trigger = document.getElementById(triggerId);
            
            // Check if click is outside the form and not on the trigger button
            if (!event.target.closest(`#${formId}`) && !event.target.closest(`#${triggerId}`)) {
                // Hide the form and show trigger
                form.style.display = 'none';
                if (trigger) trigger.style.display = 'block';
                
                // Clear form
                const inputId = `new-story-title-${sprintId}`;
                const input = document.getElementById(inputId);
                if (input) input.value = '';
            }
        }
    });
});

// Sprint 1 handler - only closes Sprint 1 form, not backlog
document.addEventListener('click', function(event) {
    const form = document.getElementById('create-story-form');
    const trigger = document.getElementById('create-story-trigger');
    
    // Only if form is visible and this is Sprint 1
    if (form && form.style.display === 'block') {
        // Check if click is outside the form and not on the trigger button
        if (!event.target.closest('#create-story-form') && !event.target.closest('#create-story-trigger')) {
            hideCreateStoryForm();
        }
    }
});

// Backlog form handler - specific for backlog only
document.addEventListener('click', function(event) {
    const form = document.getElementById('backlog-create-story-form');
    const trigger = document.getElementById('backlog-create-story-trigger');
    
    // Only if backlog form is visible
    if (form && form.style.display === 'block') {
        // Check if click is outside the backlog form and not on the backlog trigger button
        if (!event.target.closest('#backlog-create-story-form') && !event.target.closest('#backlog-create-story-trigger')) {
            hideCreateStoryFormBacklog();
        }
    }
});

// Close sprint options menus when clicking outside
document.addEventListener('click', function(event) {
    if (!event.target.closest('.sprint-menu-container')) {
        document.querySelectorAll('.sprint-options-menu').forEach(menu => {
            menu.style.display = 'none';
        });
    }
});

function setupBacklogListener() {
    const select = document.getElementById('backlog-project-select');
    if (select) select.addEventListener('change', loadBacklog);
}

async function handleCreateStory(e) {
    e.preventDefault();
    
    const id = document.getElementById('story-id').value;
    const isEdit = !!id;
    const projectFromHidden = document.getElementById('story-edit-project-id')?.value?.trim();
    const projectId = isEdit && projectFromHidden
        ? projectFromHidden
        : (document.getElementById('backlog-project-select')?.value || selectedProjectId || projects[0]?.id);

    const sprintRaw = document.getElementById('story-edit-sprint-id')?.value?.trim();
    const statusRaw = document.getElementById('story-edit-status')?.value?.trim();
    const assigneeRaw = document.getElementById('story-edit-assignee-id')?.value?.trim();

    const data = {
        projectId,
        title: document.getElementById('story-title').value,
        description: document.getElementById('story-description').value,
        acceptanceCriteria: document.getElementById('story-criteria').value,
        storyPoints: parseInt(document.getElementById('story-points').value) || 0,
        priority: parseInt(document.getElementById('story-priority').value) || 0
    };

    if (isEdit) {
        data.sprintId = sprintRaw ? sprintRaw : null;
        data.status = statusRaw || 'Backlog';
        data.assigneeId = assigneeRaw || null;
    } else {
        data.sprintId = null;
    }

    try {
        const url = isEdit ? `/api/stories/${id}` : '/api/stories';
        const method = isEdit ? 'PUT' : 'POST';
        await apiRequest(url, { method, body: JSON.stringify(data) });
        
        hideModal('story-modal');
        e.target.reset();
        document.getElementById('story-id').value = '';
        document.getElementById('story-edit-project-id').value = '';
        document.getElementById('story-edit-sprint-id').value = '';
        document.getElementById('story-edit-status').value = '';
        document.getElementById('story-edit-assignee-id').value = '';
        loadBacklog();
        refreshCurrentBoardView();
        showToast(isEdit ? 'Historia actualizada' : 'Historia creada');
    } catch (error) {
        showToast('Error: ' + error.message, 'error');
    }
}

async function editStory(id) {
    try {
        const story = await apiRequest(`/api/stories/${id}`);
        document.getElementById('story-id').value = story.id;
        document.getElementById('story-edit-project-id').value = story.projectId || '';
        document.getElementById('story-edit-sprint-id').value = story.sprintId || '';
        document.getElementById('story-edit-status').value = story.status || 'Backlog';
        document.getElementById('story-edit-assignee-id').value = story.assigneeId || '';
        document.getElementById('story-title').value = story.title;
        document.getElementById('story-description').value = story.description || '';
        document.getElementById('story-criteria').value = story.acceptanceCriteria || '';
        document.getElementById('story-points').value = story.storyPoints || '';
        document.getElementById('story-priority').value = story.priority;
        showModal('story-modal');
    } catch (error) {
        showToast('Error al cargar historia', 'error');
    }
}

async function deleteStory(id) {
    storyToDelete = id;
    showModal('delete-story-modal');
}

// Confirm delete story from modal
async function confirmDeleteStory() {
    if (!storyToDelete) return;
    
    try {
        await apiRequest(`/api/stories/${storyToDelete}`, { method: 'DELETE' });
        hideModal('delete-story-modal');
        storyToDelete = null;
        
        // Refresh current view
        const currentPage = window.location.hash.substring(1);
        if (currentPage.startsWith('board')) {
            refreshCurrentBoardView();
        } else {
            loadBacklog();
        }
        
        showToast('Historia eliminada');
    } catch (error) {
        showToast('Error al eliminar', 'error');
    }
}

// ==================== KANBAN BOARD ====================
let boardMembers = [];
let currentIssueDetail = null;
let issueActivityFilter = 'Todo';

async function loadBoard() {
    if (!projects.length) await loadProjects();
    
    const projectSelect = document.getElementById('board-project-select');
    if (projectSelect && projects.length) {
        const current = projectSelect.value;
        projectSelect.innerHTML = '<option value="">Seleccionar Proyecto</option>' + 
            projects.map(p => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('');
        if (current && projects.find(p => p.id === current)) {
            projectSelect.value = current;
        } else if (projects.length) {
            projectSelect.value = projects[0].id;
        }
    }
    
    const projectId = projectSelect?.value || projects[0]?.id;
    if (!projectId) return;
    
    try {
        const data = await apiRequest(`/api/stories/project/${projectId}/board`);
        boardMembers = data.members || [];
        const pageBoard = document.getElementById('page-board');
        let hintEl = document.getElementById('board-active-sprint-hint');
        if (pageBoard && data.hasActiveSprint === false) {
            if (!hintEl) {
                hintEl = document.createElement('div');
                hintEl.id = 'board-active-sprint-hint';
                hintEl.className = 'board-sprint-hint';
                hintEl.setAttribute('role', 'status');
                const kb = pageBoard.querySelector('.kanban-board');
                if (kb && kb.parentNode) kb.parentNode.insertBefore(hintEl, kb);
            }
            hintEl.innerHTML = '<i class="fas fa-info-circle"></i> <span>No hay sprint activo. Abre el proyecto, ve al backlog y pulsa <strong>Iniciar sprint</strong> para ver historias en el tablero.</span>';
            hintEl.style.display = 'flex';
        } else if (hintEl) {
            hintEl.style.display = 'none';
        }
        renderKanban(data.stories || [], data.members || []);
    } catch (error) {
        console.error('Error loading board:', error);
        showToast('Error al cargar tablero', 'error');
    }
}

function setupBoardListener() {
    const select = document.getElementById('board-project-select');
    if (select) select.addEventListener('change', loadBoard);
}

function renderKanban(stories, members) {
    console.log('=== renderKanban ===');
    console.log('Stories to render:', stories?.length || 0);
    console.log('Stories:', stories);
    
    // Clear all columns
    ['backlog', 'in-progress', 'done'].forEach(col => {
        const container = document.getElementById(`column-${col}`);
        if (container) container.innerHTML = '';
    });
    
    // Count stories per column
    const counts = { Backlog: 0, InProgress: 0, Done: 0 };
    
    stories.forEach((story, index) => {
        // Map old statuses to new ones
        let status = story.status;
        if (status === 'SprintBacklog') status = 'Backlog';
        
        console.log(`Story ${index}:`, story.id, 'status:', status, 'title:', story.title?.substring(0, 20));
        
        counts[status] = (counts[status] || 0) + 1;
        
        let columnId;
        switch (status) {
            case 'Backlog': columnId = 'column-backlog'; break;
            case 'InProgress': columnId = 'column-in-progress'; break;
            case 'Done': columnId = 'column-done'; break;
            default: 
                console.log('Unknown status:', status, '- defaulting to backlog');
                columnId = 'column-backlog';
        }
        
        const container = document.getElementById(columnId);
        if (container) {
            try {
                const card = createKanbanCard(story, members);
                container.appendChild(card);
                console.log('Card added to', columnId);
            } catch (cardError) {
                console.error('Error creating card for story', story.id, cardError);
            }
        } else {
            console.error('Container not found:', columnId);
        }
    });
    
    // Update counts (check if elements exist)
    const backlogCount = document.getElementById('count-backlog');
    const inProgressCount = document.getElementById('count-in-progress');
    const doneCount = document.getElementById('count-done');
    
    if (backlogCount) backlogCount.textContent = counts.Backlog || 0;
    if (inProgressCount) inProgressCount.textContent = counts.InProgress || 0;
    if (doneCount) doneCount.textContent = counts.Done || 0;
}

function createKanbanCard(story, members) {
    const card = document.createElement('div');
    card.className = 'kanban-card';
    card.draggable = true;
    card.dataset.storyId = story.id;
    card.tabIndex = 0;
    card.setAttribute('role', 'button');
    card.setAttribute('aria-label', `Abrir ${story.title}`);
    
    // Generate avatar HTML for assignee (clickable)
    const assignee = members.find(m => m.id === story.assigneeId);
    const assigneeInitials = assignee ? assignee.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2) : '?';
    const assigneeColor = assignee ? getUserColor(assignee.id) : null;
    const assigneeAvatar = assignee 
        ? `<div class="kanban-card-assignee assigned" style="background: ${assigneeColor};" data-story-id="${story.id}" onclick="showAssigneeDropdown(event, '${story.id}')" title="${escapeHtml(assignee.name)} (${assignee.role}) - Click para reasignar">${assigneeInitials}</div>`
        : `<div class="kanban-card-assignee unassigned" data-story-id="${story.id}" onclick="showAssigneeDropdown(event, '${story.id}')" title="Sin asignar - Click para asignar">?</div>`;
    
    // Generate member avatars row (like Jira)
    const memberAvatars = members.slice(0, 3).map((m, i) => {
        const initials = m.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2);
        const isAssigned = m.id === story.assigneeId;
        const userColor = getUserColor(m.id);
        return `<div class="member-avatar ${isAssigned ? 'active' : ''}" title="${escapeHtml(m.name)}" style="z-index: ${10-i}; margin-left: ${i > 0 ? '-8px' : '0'}; background: ${userColor};">${initials}</div>`;
    }).join('');
    
    const extraMembers = members.length > 3 ? `<div class="member-avatar more" style="margin-left: -8px;">+${members.length - 3}</div>` : '';
    
    // Generate story-key like in backlog
    const project = projects.find(p => p.id === story.projectId);
    let projectKey = 'PROJ';
    if (project) {
        if (project.key) {
            projectKey = project.key;
        } else {
            const nameParts = project.name.split(' ');
            if (nameParts.length > 1) {
                projectKey = nameParts.map(p => p[0]).join('').toUpperCase().substring(0, 4);
            } else {
                projectKey = project.name.substring(0, 4).toUpperCase();
            }
        }
    }
    const storyNum = story.id.length > 4 ? story.id.slice(-3) : story.id.substring(0, 3);
    const storyKey = `${projectKey}-${storyNum}`;
    
    // Default priority to 2 (medium) if not set
    const priority = story.priority || 2;
    
    card.innerHTML = `
        <div class="kanban-card-title">${escapeHtml(story.title)}</div>
        <div class="kanban-card-header">
            <span class="kanban-card-id">${storyKey}</span>
            <div class="kanban-card-priority priority-${priority}"></div>
        </div>
        <div class="kanban-card-members">
            ${memberAvatars}${extraMembers}
        </div>
        <div class="kanban-card-footer">
            <div class="kanban-card-points">
                <i class="fas fa-star"></i>
                <span>${story.storyPoints || 0}</span>
            </div>
            ${assigneeAvatar}
        </div>
        <div class="kanban-card-actions">
            <button class="btn btn-icon btn-small" onclick="editStory('${story.id}')">
                <i class="fas fa-edit"></i>
            </button>
            <button class="btn btn-icon btn-small text-danger" onclick="deleteStoryKanban('${story.id}')">
                <i class="fas fa-trash"></i>
            </button>
        </div>
    `;
    
    card.addEventListener('dragstart', (e) => {
        card.classList.add('dragging');
        const col = card.closest('.kanban-column');
        const sourceColumnStatus = col ? col.getAttribute('data-status') || '' : '';
        e.dataTransfer.setData('storyId', story.id);
        e.dataTransfer.setData('sourceColumnStatus', sourceColumnStatus);
        e.dataTransfer.effectAllowed = 'move';
    });
    
    card.addEventListener('dragend', () => {
        card.classList.remove('dragging');
        document.querySelectorAll('.kanban-column-content').forEach(col => {
            col.classList.remove('drag-over');
        });
    });

    card.addEventListener('click', (e) => {
        if (e.target.closest('button, a, input, select, textarea')) return;
        openIssueDetail(story.id);
    });

    card.addEventListener('keydown', (e) => {
        if (e.key !== 'Enter' && e.key !== ' ') return;
        e.preventDefault();
        openIssueDetail(story.id);
    });
    
    return card;
}

// Generate consistent color for a user based on their ID
function getUserColor(userId) {
    const colors = [
        '#6366f1', '#8b5cf6', '#a855f7', '#d946ef', '#ec4899',
        '#f43f5e', '#e11d48', '#f97316', '#f59e0b', '#eab308',
        '#84cc16', '#22c55e', '#10b981', '#14b8a6', '#06b6d4',
        '#0ea5e9', '#3b82f6', '#6366f1', '#4f46e5', '#7c3aed'
    ];
    let hash = 0;
    for (let i = 0; i < userId.length; i++) {
        hash = ((hash << 5) - hash) + userId.charCodeAt(i);
        hash = hash & hash;
    }
    return colors[Math.abs(hash) % colors.length];
}

// Show dropdown for assignee selection on kanban card
function showAssigneeDropdown(event, storyId) {
    event.stopPropagation();
    event.preventDefault();
    
    // Close any existing dropdowns
    document.querySelectorAll('.assignee-dropdown').forEach(d => d.remove());
    
    // Get the project and members
    const project = projects.find(p => p.id === selectedProjectId);
    const members = project?.members || [];
    
    // Get the story
    const storyElement = document.querySelector(`[data-story-id="${storyId}"]`);
    if (!storyElement) return;
    
    // Create dropdown HTML
    const dropdown = document.createElement('div');
    dropdown.className = 'assignee-dropdown';
    dropdown.dataset.storyId = storyId;
    
    // Build member options
    let optionsHtml = `
        <div class="assignee-option ${!storyElement.dataset.assigneeId ? 'selected' : ''}" onclick="assignStoryToMember('${storyId}', null)">
            <div class="assignee-avatar unassigned">?</div>
            <span>Sin asignar</span>
        </div>
    `;
    
    members.forEach(member => {
        const initials = member.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2);
        const isAssigned = storyElement.dataset.assigneeId === member.id;
        const userColor = getUserColor(member.id);
        optionsHtml += `
            <div class="assignee-option ${isAssigned ? 'selected' : ''}" onclick="assignStoryToMember('${storyId}', '${member.id}')">
                <div class="assignee-avatar" style="background: ${userColor}; color: white;">${initials}</div>
                <div class="assignee-info">
                    <span class="assignee-name">${escapeHtml(member.name)}</span>
                    <span class="assignee-email">${escapeHtml(member.email)}</span>
                </div>
            </div>
        `;
    });
    
    dropdown.innerHTML = optionsHtml;
    
    // Position dropdown near the clicked element
    const rect = event.target.getBoundingClientRect();
    dropdown.style.position = 'fixed';
    dropdown.style.left = `${rect.left}px`;
    dropdown.style.top = `${rect.bottom + 5}px`;
    dropdown.style.zIndex = '9999';
    
    document.body.appendChild(dropdown);
    
    // Close dropdown when clicking outside
    const closeDropdown = (e) => {
        if (!dropdown.contains(e.target) && e.target !== event.target) {
            dropdown.remove();
            document.removeEventListener('click', closeDropdown);
        }
    };
    
    setTimeout(() => {
        document.addEventListener('click', closeDropdown);
    }, 10);
}

// Assign story to a member
async function assignStoryToMember(storyId, memberId) {
    // Close dropdown
    document.querySelectorAll('.assignee-dropdown').forEach(d => d.remove());
    
    try {
        // Get current story data
        const story = await apiRequest(`/api/stories/${storyId}`);
        
        // Update the story with new assignee (keep current sprintId and status)
        await apiRequest(`/api/stories/${storyId}`, {
            method: 'PUT',
            body: JSON.stringify({
                projectId: story.projectId,
                title: story.title,
                description: story.description,
                priority: story.priority,
                storyPoints: story.storyPoints,
                assigneeId: memberId,
                sprintId: story.sprintId,
                status: story.status
            })
        });
        
        // Update the card's assignee display without reloading the board
        const project = projects.find(p => p.id === selectedProjectId);
        const member = memberId ? project?.members?.find(m => m.id === memberId) : null;
        const assigneeInitials = member ? member.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2) : '?';
        const assigneeColor = member ? getUserColor(member.id) : null;
        
        // Find and update the assignee avatar in the card
        const card = document.querySelector(`.kanban-card[data-story-id="${storyId}"]`);
        
        if (card) {
            const assigneeAvatar = card.querySelector('.kanban-card-assignee');
            if (assigneeAvatar) {
                if (member) {
                    assigneeAvatar.className = 'kanban-card-assignee assigned';
                    assigneeAvatar.style.background = assigneeColor;
                    assigneeAvatar.textContent = assigneeInitials;
                    assigneeAvatar.title = `${escapeHtml(member.name)} (${member.role}) - Click para reasignar`;
                    assigneeAvatar.dataset.assigneeId = member.id;
                } else {
                    assigneeAvatar.className = 'kanban-card-assignee unassigned';
                    assigneeAvatar.textContent = '?';
                    assigneeAvatar.title = 'Sin asignar - Click para asignar';
                    assigneeAvatar.dataset.assigneeId = '';
                }
            }
        }
        
        showToast(member ? `Asignado a ${member.name}` : 'Sin asignar', 'success');
    } catch (error) {
        console.error('Error assigning story:', error);
        showToast('Error al asignar historia', 'error');
    }
}

async function openIssueDetail(storyId) {
    const loading = document.getElementById('issue-detail-loading');
    const body = document.getElementById('issue-detail-body');
    if (loading) loading.style.display = 'block';
    if (body) body.style.display = 'none';
    showModal('issue-detail-modal');

    try {
        // Check cache first to avoid overwriting local changes
        const storyCacheKey = `story_${storyId}`;
        let story = taskCache.get(storyCacheKey);
        
        if (!story) {
            story = await apiRequest(`/api/stories/${storyId}`);
            // Cache the story for future use
            taskCache.set(storyCacheKey, story);
        }
        
        // Always ensure the story is cached (even if loaded from cache, refresh it)
        taskCache.set(storyCacheKey, story);
        
        if (story.projectId) {
            try {
                const boardData = await apiRequest(`/api/stories/project/${story.projectId}/board`);
                boardMembers = boardData.members || [];
            } catch (e) {
                console.warn('No se pudieron cargar miembros del proyecto para el detalle:', e);
                boardMembers = [];
            }
        } else {
            boardMembers = [];
        }
        currentIssueDetail = story;
        renderIssueDetail(story);
    } catch (error) {
        hideModal('issue-detail-modal');
        showToast('Error al cargar la actividad', 'error');
    }
}

function renderIssueDetail(story) {
    const loading = document.getElementById('issue-detail-loading');
    const body = document.getElementById('issue-detail-body');
    const project = projects.find(p => p.id === story.projectId);
    const issueKey = story.key || `${project?.key || 'PROY'}-${story.id.substring(0, 4).toUpperCase()}`;
    const createdBy = boardMembers.find(m => m.id === story.createdById) || (currentUser?.id === story.createdById ? currentUser : null);

    document.getElementById('issue-detail-key').textContent = issueKey;
    document.getElementById('issue-detail-title').value = story.title || '';
    document.getElementById('issue-detail-description').value = story.description || '';
    document.getElementById('issue-detail-status').value = story.status === 'SprintBacklog' ? 'Backlog' : story.status;
    document.getElementById('issue-detail-priority').value = story.priority ?? 2;
    document.getElementById('issue-detail-points').value = story.storyPoints ?? 0;
    document.getElementById('issue-detail-sprint').textContent = story.sprintId ? `Sprint ${story.sprintId.substring(0, 8)}` : 'Ninguno';
    document.getElementById('issue-detail-reporter').textContent = createdBy?.name || currentUser?.name || 'Ninguno';
    document.getElementById('issue-current-user-avatar').textContent = getInitials(currentUser?.name || 'Usuario');
    document.getElementById('issue-detail-created').textContent = `Creado: ${formatIssueDate(story.createdAt)}`;
    document.getElementById('issue-detail-updated').textContent = `Actualizado: ${formatIssueDate(story.updatedAt || story.createdAt)}`;

    renderIssueAssigneeSelect(story);
    renderIssueSubtaskAssigneeSelect();
    renderIssueSubtasks(story.tasks || []);
    renderIssueActivity(story);

    if (loading) loading.style.display = 'none';
    if (body) body.style.display = 'grid';
}

function setIssueActivityFilter(filter, buttonEl) {
    issueActivityFilter = filter;
    const tabs = document.querySelectorAll('.issue-activity-tabs button');
    tabs.forEach(btn => btn.classList.remove('active'));
    if (buttonEl) buttonEl.classList.add('active');
    if (currentIssueDetail) renderIssueActivity(currentIssueDetail);
}

function renderIssueActivity(story) {
    const list = document.getElementById('issue-activity-list');
    if (!list) return;

    const comments = (story.comments || []).map(comment => ({
        kind: 'Comentario',
        userName: comment.userName || 'Usuario',
        message: comment.message,
        createdAt: comment.createdAt
    }));
    const history = (story.history || []).map(item => ({
        kind: 'Historial',
        userName: item.userName || 'Sistema',
        message: item.message,
        createdAt: item.createdAt
    }));

    let items = [...comments, ...history];
    if (issueActivityFilter === 'Comentarios') items = comments;
    if (issueActivityFilter === 'Historial') items = history;

    items.sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));

    if (!items.length) {
        list.innerHTML = `<div class="issue-empty">Sin actividad registrada.</div>`;
        return;
    }

    list.innerHTML = items.map(item => `
        <div class="issue-history-row">
            <div class="issue-avatar">${getInitials(item.userName)}</div>
            <div>
                <div><strong>${escapeHtml(item.userName)}</strong> <span class="issue-activity-kind">${item.kind}</span></div>
                <div>${escapeHtml(item.message || '')}</div>
                <small>${formatIssueDateTime(item.createdAt)}</small>
            </div>
        </div>
    `).join('');
}

async function addIssueComment() {
    if (!currentIssueDetail) return;
    const input = document.getElementById('issue-new-comment');
    const message = input?.value?.trim();

    if (!message) {
        showToast('Escribe un comentario', 'error');
        return;
    }

    try {
        await apiRequest(`/api/stories/${currentIssueDetail.id}/comments`, {
            method: 'POST',
            body: JSON.stringify({
                userId: currentUser?.id || '',
                message
            })
        });
        if (input) input.value = '';
        // Only reload if not prevented (e.g., after assignment)
        if (!preventIssueDetailReload) {
            const story = await apiRequest(`/api/stories/${currentIssueDetail.id}`);
            currentIssueDetail = story;
            renderIssueDetail(story);
        }
        showToast('Comentario agregado');
    } catch (error) {
        showToast('Error al agregar comentario', 'error');
    }
}

function renderIssueAssigneeSelect(story) {
    const assigneeSelect = document.getElementById('issue-detail-assignee');
    if (!assigneeSelect) return;

    assigneeSelect.innerHTML = '<option value="">Sin asignar</option>' +
        boardMembers.map(member => `<option value="${member.id}">${escapeHtml(member.name)}</option>`).join('');
    assigneeSelect.value = story.assigneeId || '';
}

function renderIssueSubtaskAssigneeSelect() {
    const list = document.getElementById('issue-members-list');
    if (!list) return;

    list.innerHTML = boardMembers
        .map(member => `<option value="${escapeHtml(member.name)}"></option>`)
        .join('');
}

function renderIssueSubtasks(tasks) {
    const list = document.getElementById('issue-subtasks-list');
    const progressBar = document.getElementById('issue-subtask-progress-bar');
    const progressText = document.getElementById('issue-subtask-progress-text');
    const completed = tasks.filter(task => task.status === 'Done').length;
    const inProgress = tasks.filter(task => task.status === 'InProgress').length;
    const totalActive = completed + inProgress;
    
    // Calcular progreso: 50% para tareas en curso, 100% para finalizadas
    let progressPercent = 0;
    if (tasks.length > 0) {
        const completedWeight = completed * 100;
        const inProgressWeight = inProgress * 50;
        progressPercent = Math.round((completedWeight + inProgressWeight) / tasks.length);
    }

    if (progressBar) {
        progressBar.style.width = `${progressPercent}%`;
        // Color según progreso
        if (progressPercent === 0) {
            progressBar.style.background = '#6b7280'; // gris para POR HACER
        } else if (progressPercent === 100) {
            progressBar.style.background = '#22c55e'; // verde para FINALIZADO
        } else {
            progressBar.style.background = '#3b82f6'; // azul para EN CURSO
        }
    }
    
    if (progressText) progressText.textContent = `${progressPercent}% completado`;
    if (!list) return;

    if (!tasks.length) {
        list.innerHTML = '';
        return;
    }

    list.innerHTML = `
        <div class="issue-subtask-table">
            <div class="issue-subtask-head">
                <span>Actividad</span>
                <span>Prioridad</span>
                <span>Persona asignada</span>
                <span>Estado</span>
            </div>
            ${tasks.map((task, index) => {
                // Generate project-key like in kanban cards
                const project = projects.find(p => p.id === currentIssueDetail.projectId);
                let projectKey = 'PROJ';
                if (project) {
                    if (project.key) {
                        projectKey = project.key;
                    } else {
                        const nameParts = project.name.split(' ');
                        if (nameParts.length > 1) {
                            projectKey = nameParts.map(p => p[0]).join('').toUpperCase().substring(0, 4);
                        } else {
                            projectKey = project.name.substring(0, 4).toUpperCase();
                        }
                    }
                }
                const taskNum = task.id.length > 4 ? task.id.slice(-3) : task.id.substring(0, 3);
                const taskKey = `${projectKey}-${taskNum}`;
                const assignedMember = boardMembers.find(m => m.id === task.assignedToId);
                const priorityLabels = { 0: 'Baja', 1: 'Media', 2: 'Alta', 3: 'Critica' };
                const priorityColors = { 0: '#22c55e', 1: '#3b82f6', 2: '#f59e0b', 3: '#ef4444' };
                const statusLabels = { Todo: 'POR HACER', InProgress: 'EN CURSO', Done: 'FINALIZADO' };
                const statusColors = { Todo: '#6b7280', InProgress: '#3b82f6', Done: '#22c55e' };
                return `
                    <div class="issue-subtask-row" data-task-id="${task.id}">
                        <div class="issue-subtask-title">
                            <i class="fas fa-link"></i>
                            <a href="#" onclick="openSubtaskDetail('${task.id}', '${taskKey}'); return false;" class="subtask-key-link" title="Ver detalle de ${taskKey}">${taskKey}</a>
                            <input type="text" value="${escapeHtml(task.title)}" maxlength="255"
                                onchange="updateIssueSubtaskDetails('${task.id}')"
                                onblur="updateIssueSubtaskDetails('${task.id}')">
                        </div>
                        <div class="issue-priority-badge" style="color: ${priorityColors[task.priority ?? 1]}">
                            <i class="fas fa-equals" style="margin-right: 4px;"></i>${priorityLabels[task.priority ?? 1]}
                        </div>
                        <div class="issue-assignee-picker" data-task-id="${task.id}">
                            <div class="issue-assignee-trigger" onclick="toggleIssueSubtaskAssigneePicker('${task.id}')">
                                ${assignedMember 
                                    ? `<span class="subtask-avatar assigned" style="background: ${getUserColor(assignedMember.id)} !important;">${getInitials(assignedMember.name)}</span>`
                                    : `<span class="subtask-avatar unassigned">?</span>`
                                }
                                <span>${assignedMember ? escapeHtml(assignedMember.name.substring(0, 15)) : 'Sin asignar'}</span>
                            </div>
                            <div class="issue-assignee-menu" id="issue-assignee-menu-${task.id}" style="display: none;">
                                <input type="text" placeholder="Buscar miembro..." oninput="filterIssueSubtaskAssigneePicker('${task.id}', this.value)" onclick="event.stopPropagation()">
                                <button type="button" class="issue-assignee-option" data-member-name="Sin asignar" onclick="selectIssueSubtaskAssigneeFromPicker('${task.id}', '', this)">
                                    <span class="issue-assignee-option-main">
                                        <span class="issue-assignee-option-avatar unassigned">?</span>
                                        <span>Sin asignar</span>
                                    </span>
                                    <span class="issue-assignee-option-role">Sin responsable</span>
                                </button>
                                ${boardMembers.map(member => `
                                    <button type="button" class="issue-assignee-option" data-member-name="${escapeHtml(member.name)}" onclick="selectIssueSubtaskAssigneeFromPicker('${task.id}', '${member.id}', this)">
                                        <span class="issue-assignee-option-main">
                                            <span class="issue-assignee-option-avatar assigned" style="background: ${getUserColor(member.id)} !important;">${escapeHtml(getInitials(member.name || 'U'))}</span>
                                            <span>${escapeHtml(member.name)}</span>
                                        </span>
                                        <span class="issue-assignee-option-email">${escapeHtml(member.email || '')}</span>
                                    </button>
                                `).join('')}
                            </div>
                        </div>
                        <div class="issue-status-badge-wrapper">
                            <select class="issue-status-badge" onchange="updateIssueSubtaskStatus('${task.id}', this.value)" 
                                style="background-color: ${statusColors[task.status]}20; color: ${statusColors[task.status]}; border-color: ${statusColors[task.status]}40;">
                                <option value="Todo" ${task.status === 'Todo' ? 'selected' : ''} style="background: #2d2d3a;">POR HACER</option>
                                <option value="InProgress" ${task.status === 'InProgress' ? 'selected' : ''} style="background: #2d2d3a;">EN CURSO</option>
                                <option value="Done" ${task.status === 'Done' ? 'selected' : ''} style="background: #2d2d3a;">FINALIZADO</option>
                            </select>
                        </div>
                    </div>
                `;
            }).join('')}
        </div>
    `;
}

async function saveIssueDetail() {
    if (!currentIssueDetail) return;

    const data = {
        projectId: currentIssueDetail.projectId,
        sprintId: currentIssueDetail.sprintId,
        title: document.getElementById('issue-detail-title').value.trim(),
        description: document.getElementById('issue-detail-description').value.trim(),
        acceptanceCriteria: currentIssueDetail.acceptanceCriteria || '',
        storyPoints: parseInt(document.getElementById('issue-detail-points').value) || 0,
        priority: parseInt(document.getElementById('issue-detail-priority').value),
        assigneeId: document.getElementById('issue-detail-assignee').value || null,
        status: document.getElementById('issue-detail-status').value
    };

    if (!data.title) {
        showToast('El titulo es obligatorio', 'error');
        return;
    }

    try {
        const story = await apiRequest(`/api/stories/${currentIssueDetail.id}`, {
            method: 'PUT',
            body: JSON.stringify(data)
        });
        
        // Preservar las subtareas existentes al actualizar los datos principales
        if (story && currentIssueDetail) {
            // Actualizar solo los campos principales, mantener las subtareas
            Object.assign(currentIssueDetail, {
                title: story.title,
                description: story.description,
                acceptanceCriteria: story.acceptanceCriteria,
                storyPoints: story.storyPoints,
                priority: story.priority,
                assigneeId: story.assigneeId,
                status: story.status,
                assigneeName: story.assigneeName
            });
        }
        
        renderIssueDetail(currentIssueDetail);
        refreshCurrentBoardView();
        showToast('Actividad actualizada');
    } catch (error) {
        showToast('Error al guardar actividad', 'error');
    }
}

async function updateIssueStatusFromDetail() {
    if (!currentIssueDetail) return;
    const status = document.getElementById('issue-detail-status').value;

    try {
        await apiRequest(`/api/stories/${currentIssueDetail.id}/status`, {
            method: 'PUT',
            body: JSON.stringify({ status })
        });
        currentIssueDetail.status = status;
        refreshCurrentBoardView();
        showToast(`Movido a ${getStatusText(status)}`);
    } catch (error) {
        showToast('Error al mover actividad', 'error');
    }
}

async function createIssueSubtask() {
    if (!currentIssueDetail) return;
    const input = document.getElementById('issue-new-subtask-title');
    const assigneeInput = document.getElementById('issue-new-subtask-assignee');
    const prioritySelect = document.getElementById('issue-new-subtask-priority');
    const title = input?.value.trim();
    if (!title) return;

    const assigneeId = resolveIssueMemberIdByName(assigneeInput?.value || '');
    if ((assigneeInput?.value || '').trim() && !assigneeId) {
        showToast('Selecciona un miembro valido del proyecto', 'error');
        return;
    }

    try {
        const createdTask = await apiRequest('/api/tasks/', {
            method: 'POST',
            body: JSON.stringify({
                storyId: currentIssueDetail.id,
                title,
                description: '',
                estimatedHours: null,
                priority: parseInt(prioritySelect?.value || '1')
            })
        });

        if (assigneeId) {
            await apiRequest(`/api/tasks/${createdTask.id}/assign`, {
                method: 'PATCH',
                body: JSON.stringify(assigneeId)
            });
        }

        input.value = '';
        if (assigneeInput) assigneeInput.value = '';
        if (prioritySelect) prioritySelect.value = '1';
        const story = await apiRequest(`/api/stories/${currentIssueDetail.id}`);
        currentIssueDetail = story;
        renderIssueDetail(story);
        showToast('Subtarea creada');
    } catch (error) {
        showToast('Error al crear subtarea', 'error');
    }
}

function resolveIssueMemberIdByName(name) {
    const normalized = (name || '').trim().toLowerCase();
    if (!normalized) return '';
    const member = boardMembers.find(m => (m.name || '').trim().toLowerCase() === normalized);
    return member?.id || '';
}

function toggleIssueSubtaskAssigneePicker(taskId) {
    const menu = document.getElementById(`issue-assignee-menu-${taskId}`);
    if (!menu) return;
    const isOpen = menu.classList.contains('open');
    
    // Close all other menus
    document.querySelectorAll('.issue-assignee-menu.open').forEach(m => m.classList.remove('open'));
    
    // Toggle current menu
    if (!isOpen) {
        menu.classList.add('open');
        menu.style.display = 'block';
        // Focus search input
        const search = menu.querySelector('input');
        if (search) {
            search.value = '';
            filterIssueSubtaskAssigneePicker(taskId, '');
            search.focus();
        }
    } else {
        menu.classList.remove('open');
        menu.style.display = 'none';
    }
}

function closeAllIssueSubtaskAssigneePickers() {
    document.querySelectorAll('.issue-assignee-menu.open').forEach(menu => {
        menu.classList.remove('open');
        menu.style.display = 'none';
    });
}

function filterIssueSubtaskAssigneePicker(taskId, query) {
    const menu = document.getElementById(`issue-assignee-menu-${taskId}`);
    if (!menu) return;
    const q = (query || '').trim().toLowerCase();
    menu.querySelectorAll('.issue-assignee-option').forEach(option => {
        const name = (option.dataset.memberName || '').toLowerCase();
        option.style.display = !q || name.includes(q) ? 'block' : 'none';
    });
}

function selectIssueSubtaskAssigneeFromPicker(taskId, assigneeId, buttonEl) {
    const row = document.querySelector(`.issue-subtask-row[data-task-id="${taskId}"]`);
    const trigger = row?.querySelector('.issue-assignee-trigger');
    const avatar = trigger?.querySelector('.subtask-avatar');
    const nameSpan = trigger?.querySelector('span:not(.subtask-avatar)');
    
    if (trigger && avatar && nameSpan && buttonEl) {
        const selectedName = buttonEl?.dataset?.memberName || 'Sin asignar';
        const member = boardMembers.find(m => m.id === assigneeId);
        
        // Update the name span
        nameSpan.textContent = selectedName;
        
        // Update the avatar
        if (member) {
            avatar.textContent = getInitials(member.name);
            avatar.className = 'subtask-avatar assigned';
            const userColor = getUserColor(member.id);
            avatar.style.setProperty('--user-color', userColor);
            avatar.style.background = userColor;
        } else {
            avatar.textContent = '?';
            avatar.className = 'subtask-avatar unassigned';
            avatar.style.removeProperty('--user-color');
            avatar.style.background = '';
        }
    }
    
    closeAllIssueSubtaskAssigneePickers();
    updateIssueSubtaskAssignee(taskId, assigneeId);
    
    // Update task object in currentIssueDetail to reflect the assignment
    if (currentIssueDetail && currentIssueDetail.subtasks) {
        const subtask = currentIssueDetail.subtasks.find(st => st.id === taskId);
        if (subtask) {
            subtask.assignedToId = assigneeId;
            subtask.assignedToName = member ? member.name : 'Sin asignar';
        }
    }
}

function closeAllIssueSubtaskAssigneePickers() {
    document.querySelectorAll('.issue-assignee-menu.open').forEach(menu => {
        menu.classList.remove('open');
    });
}

async function createIssueSubtask() {
    if (!currentIssueDetail) return;
    
    const titleInput = document.getElementById('issue-new-subtask-title');
    const prioritySelect = document.getElementById('issue-new-subtask-priority');
    const assigneeInput = document.getElementById('issue-new-subtask-assignee');
    
    const title = titleInput?.value?.trim();
    if (!title) {
        showToast('El titulo de la subtarea es obligatorio', 'error');
        if (titleInput) titleInput.focus();
        return;
    }
    
    const priority = parseInt(prioritySelect?.value ?? 1);
    const assigneeName = assigneeInput?.value?.trim();
    const assignee = assigneeName ? boardMembers.find(m => m.name === assigneeName) : null;
    
    try {
        await apiRequest('/api/tasks', {
            method: 'POST',
            body: JSON.stringify({
                storyId: currentIssueDetail.id,
                title,
                priority,
                assignedToId: assignee?.id || ''
            })
        });
        
        // Clear inputs
        if (titleInput) titleInput.value = '';
        if (prioritySelect) prioritySelect.value = '1';
        if (assigneeInput) assigneeInput.value = '';
        
        // Refresh story data only if not prevented
        if (!preventIssueDetailReload) {
            const story = await apiRequest(`/api/stories/${currentIssueDetail.id}`);
            currentIssueDetail = story;
            renderIssueDetail(story);
        }
        showToast('Subtarea creada');
    } catch (error) {
        showToast('Error al crear subtarea', 'error');
    }
}

async function updateIssueSubtaskDetails(taskId) {
    if (!currentIssueDetail) return;
    const task = (currentIssueDetail.tasks || []).find(t => t.id === taskId);
    if (!task) return;

    const row = document.querySelector(`.issue-subtask-row[data-task-id="${taskId}"]`);
    const titleInput = row?.querySelector('.issue-subtask-title input');
    const prioritySelect = document.getElementById(`issue-subtask-priority-${taskId}`);
    const title = titleInput?.value?.trim();

    if (!title) {
        showToast('El titulo de la subtarea es obligatorio', 'error');
        if (titleInput) titleInput.value = task.title || '';
        return;
    }

    try {
        await apiRequest(`/api/tasks/${taskId}`, {
            method: 'PUT',
            body: JSON.stringify({
                storyId: task.storyId,
                title,
                description: task.description || '',
                estimatedHours: task.estimatedHours ?? null,
                priority: parseInt(prioritySelect?.value ?? task.priority ?? 1)
            })
        });
        const story = await apiRequest(`/api/stories/${currentIssueDetail.id}`);
        currentIssueDetail = story;
        renderIssueDetail(story);
        showToast('Subtarea actualizada');
    } catch (error) {
        showToast('Error al actualizar subtarea', 'error');
    }
}

async function updateIssueSubtaskStatus(taskId, status) {
    try {
        await apiRequest(`/api/tasks/${taskId}/status`, {
            method: 'PATCH',
            body: JSON.stringify({ status, actualHours: 0 })
        });
        const story = await apiRequest(`/api/stories/${currentIssueDetail.id}`);
        currentIssueDetail = story;
        renderIssueDetail(story);
        showToast('Subtarea actualizada');
    } catch (error) {
        showToast('Error al actualizar subtarea', 'error');
    }
}

async function updateIssueSubtaskAssignee(taskId, assigneeId) {
    try {
        const response = await apiRequest(`/api/tasks/${taskId}/assign`, {
            method: 'PATCH',
            body: { assignedToId: assigneeId || '' }
        });
        
        // Update task in currentIssueDetail without re-rendering the entire UI
        const tasks = currentIssueDetail?.tasks || currentIssueDetail?.subtasks;
        if (currentIssueDetail && tasks) {
            const subtask = tasks.find(st => st.id === taskId);
            if (subtask) {
                subtask.assignedToId = assigneeId;
                subtask.assignedToName = response.assignedToName || (assigneeId ? boardMembers.find(m => m.id === assigneeId)?.name : null);
                
                // Update task cache as well to persist changes
                const cachedTask = taskCache.get(taskId);
                if (cachedTask) {
                    cachedTask.assignedToId = assigneeId;
                    cachedTask.assignedToName = subtask.assignedToName;
                    taskCache.set(taskId, cachedTask);
                }
                
                // Update the story cache to prevent reload from API
                const storyCacheKey = `story_${currentIssueDetail.id}`;
                const cachedStory = taskCache.get(storyCacheKey);
                if (cachedStory) {
                    const storyTasks = cachedStory.tasks || cachedStory.subtasks;
                    const storySubtask = storyTasks?.find(st => st.id === taskId);
                    if (storySubtask) {
                        storySubtask.assignedToId = assigneeId;
                        storySubtask.assignedToName = subtask.assignedToName;
                        taskCache.set(storyCacheKey, cachedStory);
                    } else {
                        // If subtask not in cached story, add it
                        if (!cachedStory.tasks && !cachedStory.subtasks) {
                            cachedStory.tasks = [];
                        }
                        const targetArray = cachedStory.tasks || cachedStory.subtasks;
                        targetArray.push({
                            id: taskId,
                            assignedToId: assigneeId,
                            assignedToName: subtask.assignedToName
                        });
                        taskCache.set(storyCacheKey, cachedStory);
                    }
                } else {
                    // Create cache entry if it doesn't exist
                    const sourceTasks = currentIssueDetail.tasks || currentIssueDetail.subtasks;
                    const newStoryCache = {
                        ...currentIssueDetail,
                        tasks: sourceTasks?.map(st => 
                            st.id === taskId 
                                ? { ...st, assignedToId: assigneeId, assignedToName: subtask.assignedToName }
                                : st
                        )
                    };
                    taskCache.set(storyCacheKey, newStoryCache);
                }
                
                // Update subtask detail modal if it's open and this is the current subtask
                if (currentSubtaskDetail && currentSubtaskDetail.id === taskId) {
                    currentSubtaskDetail.assignedToId = assigneeId;
                    currentSubtaskDetail.assignedToName = subtask.assignedToName;
                    
                    // Update assignee display in modal
                    const assigneeDisplay = document.getElementById('subtask-assignee-display');
                    const assignedMember = boardMembers.find(m => m.id === assigneeId);
                    
                    if (assigneeDisplay) {
                        if (assignedMember) {
                            assigneeDisplay.innerHTML = `
                                <span class="subtask-assignee-avatar" style="background: ${getUserColor(assignedMember.id)} !important;">${getInitials(assignedMember.name)}</span>
                                <span class="subtask-assignee-name">${escapeHtml(assignedMember.name)}</span>
                            `;
                        } else {
                            assigneeDisplay.innerHTML = `
                                <span class="subtask-assignee-avatar unassigned">?</span>
                                <span class="subtask-assignee-name">Sin asignar</span>
                            `;
                        }
                    }
                }
            }
        }
        
        // Set flag to prevent reload for a short time
        preventIssueDetailReload = true;
        setTimeout(() => {
            preventIssueDetailReload = false;
        }, 2000); // Reset after 2 seconds
        
        showToast('Asignacion de subtarea actualizada');
    } catch (error) {
        console.error('Error assigning task:', error);
        showToast('Error al asignar subtarea: ' + error.message, 'error');
    }
}

async function deleteIssueSubtask(taskId) {
    if (!currentIssueDetail) return;
    if (!confirm('¿Eliminar esta subtarea?')) return;

    try {
        await apiRequest(`/api/tasks/${taskId}`, { method: 'DELETE' });
        // Only reload if not prevented (e.g., after assignment)
        if (!preventIssueDetailReload) {
            const story = await apiRequest(`/api/stories/${currentIssueDetail.id}`);
            currentIssueDetail = story;
            renderIssueDetail(story);
        }
        showToast('Subtarea eliminada');
    } catch (error) {
        showToast('Error al eliminar subtarea', 'error');
    }
}

function editIssueInClassicModal() {
    if (!currentIssueDetail) return;
    hideModal('issue-detail-modal');
    editStory(currentIssueDetail.id);
}

function refreshCurrentBoardView() {
    if (document.getElementById('project-content') && projectSubTab === 'board') {
        loadProjectBoard();
        return;
    }

    if (document.getElementById('board-project-select')) {
        loadBoard();
    }
}

function getInitials(name) {
    return name.split(' ').map(part => part[0]).join('').toUpperCase().substring(0, 2) || 'U';
}

function formatIssueDate(value) {
    if (!value) return '-';
    return new Date(value).toLocaleDateString('es-PE', {
        day: '2-digit',
        month: 'short',
        year: 'numeric'
    });
}

function formatIssueDateTime(value) {
    if (!value) return '-';
    return new Date(value).toLocaleString('es-PE', {
        day: '2-digit',
        month: 'short',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}

function getScrumRoleLabel(role) {
    if (role === null || role === undefined || role === '') return 'Dev';
    const byNumber = { 0: 'PO', 1: 'SM', 2: 'Dev' };
    if (typeof role === 'number' && byNumber[role] !== undefined) return byNumber[role];
    const key = String(role);
    const map = {
        ProductOwner: 'PO',
        ScrumMaster: 'SM',
        Developer: 'Dev',
        Owner: 'Prop.',
        Admin: 'Admin'
    };
    return map[key] || key;
}

// Event listener global para cerrar menús al hacer click fuera
document.addEventListener('click', function(event) {
    const target = event.target;
    
    // Verificar si se hizo click dentro de un menú de asignación abierto
    const clickedInsideMenu = target.closest('.issue-assignee-menu.open');
    
    // Verificar si se hizo click dentro de un trigger de asignación
    const clickedInsideTrigger = target.closest('.issue-assignee-trigger');
    
    // Verificar si se hizo click dentro de las opciones del menú
    const clickedInsideOption = target.closest('.issue-assignee-option');
    
    // Solo cerrar si se hace click fuera del menú, trigger y opciones
    if (!clickedInsideMenu && !clickedInsideTrigger && !clickedInsideOption) {
        closeAllIssueSubtaskAssigneePickers();
    }
});

// Prevenir que el menú se cierre cuando el cursor pasa sobre él
document.addEventListener('mouseover', function(event) {
    const target = event.target;
    const menu = target.closest('.issue-assignee-menu');
    
    if (menu) {
        // Asegurar que el menú permanezca opaco
        menu.style.background = '#1d1738';
        menu.style.opacity = '1';
    }
});

// Cerrar menús al presionar Escape
document.addEventListener('keydown', function(event) {
    if (event.key === 'Escape') {
        closeAllIssueSubtaskAssigneePickers();
    }
});

function allowDrop(ev) {
    ev.preventDefault();
    ev.currentTarget.classList.add('drag-over');
}

async function drop(ev, newStatus) {
    ev.preventDefault();
    ev.currentTarget.classList.remove('drag-over');
    
    const storyId = ev.dataTransfer.getData('storyId');
    if (!storyId) return;

    const fromColumn = ev.dataTransfer.getData('sourceColumnStatus');
    if (fromColumn && fromColumn === newStatus) {
        return;
    }
    
    try {
        await apiRequest(`/api/stories/${storyId}/status`, {
            method: 'PUT',
            body: JSON.stringify({ status: newStatus })
        });
        refreshCurrentBoardView();
        showToast(`Movido a ${getStatusText(newStatus)}`);
    } catch (error) {
        showToast('Error al mover historia', 'error');
    }
}

function getStatusText(status) {
    const texts = {
        'Backlog': 'Backlog',
        'SprintBacklog': 'Sprint Backlog',
        'InProgress': 'En Progreso',
        'Done': 'Completado'
    };
    return texts[status] || status;
}

async function deleteStoryKanban(id) {
    storyToDelete = id;
    showModal('delete-story-modal');
}

// Confirm delete story from Kanban (uses same confirm function as backlog)

// ==================== UTILS ====================
function getPriorityText(priority) {
    const texts = { 0: 'Baja', 1: 'Media', 2: 'Alta', 3: 'Crítica' };
    return texts[priority] || 'Media';
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function showModal(id) {
    const modal = document.getElementById(id);
    if (modal) modal.classList.add('active');
}

function hideModal(id) {
    const modal = document.getElementById(id);
    if (modal) modal.classList.remove('active');
}

function showToast(message, type = 'success') {
    const toast = document.getElementById('toast');
    if (toast) {
        toast.textContent = message;
        toast.className = `toast ${type} show`;
        setTimeout(() => toast.classList.remove('show'), 3000);
    }
}

// ==================== SUBTASK DETAIL ====================
let currentSubtaskDetail = null;

async function openSubtaskDetail(taskId, taskKey) {
    // Show loading state
    const loading = document.getElementById('subtask-detail-loading');
    const body = document.getElementById('subtask-detail-body');
    if (loading) loading.style.display = 'flex';
    if (body) body.style.display = 'none';
    
    // Show modal
    showModal('subtask-detail-modal');
    
    try {
        // Check cache first
        let task = taskCache.get(taskId);
        
        if (!task) {
            // If not found in cache, fetch from API
            const fetchedTask = await apiRequest(`/api/tasks/${taskId}`);
            taskCache.set(taskId, fetchedTask); // Cache for future use
            currentSubtaskDetail = fetchedTask;
        } else {
            currentSubtaskDetail = task;
        }
        
        renderSubtaskDetail(currentSubtaskDetail, taskKey);
    } catch (error) {
        console.error('Error loading subtask:', error);
        hideModal('subtask-detail-modal');
        showToast('Error al cargar la subtarea', 'error');
    }
}

function renderSubtaskDetail(task, taskKey) {
    const loading = document.getElementById('subtask-detail-loading');
    const body = document.getElementById('subtask-detail-body');
    
    // Get parent story info
    const parentStory = currentIssueDetail;
    const parentKey = document.getElementById('issue-detail-key')?.textContent || 'PROJ-000';
    
    // Set breadcrumbs
    document.getElementById('subtask-parent-key').textContent = parentKey;
    document.getElementById('subtask-key').textContent = taskKey;
    
    // Set title
    document.getElementById('subtask-title').textContent = task.title || 'Sin título';
    
    // Set description
    const descEl = document.getElementById('subtask-description');
    if (task.description) {
        descEl.innerHTML = task.description;
    } else {
        descEl.innerHTML = '<p class="placeholder-text">Agrega una descripción más detallada...</p>';
    }
    
    // Reset toolbar more options
    const moreOptions = document.getElementById('toolbar-more-options');
    if (moreOptions) moreOptions.style.display = 'none';
    
    // Set status
    const statusSelect = document.getElementById('subtask-status');
    if (statusSelect) statusSelect.value = task.status || 'Todo';
    
    // Set assignee
    const assigneeDisplay = document.getElementById('subtask-assignee-display');
    const assignedMember = boardMembers.find(m => m.id.toLowerCase() === task.assignedToId?.toLowerCase());
    
    if (assignedMember) {
        assigneeDisplay.innerHTML = `
            <span class="subtask-assignee-avatar" style="background: ${getUserColor(assignedMember.id)} !important;">${getInitials(assignedMember.name)}</span>
            <span class="subtask-assignee-name">${escapeHtml(assignedMember.name)}</span>
        `;
    } else {
        assigneeDisplay.innerHTML = `
            <span class="subtask-assignee-avatar unassigned">?</span>
            <span class="subtask-assignee-name">Sin asignar</span>
        `;
    }
    
    // Set parent story link
    const parentLink = document.getElementById('subtask-parent-link');
    const parentTitle = document.getElementById('subtask-parent-title');
    if (parentLink && parentStory) {
        parentLink.onclick = () => {
            closeSubtaskDetail();
            // Parent is already open, just close this modal
            return false;
        };
        parentTitle.textContent = parentStory.title || 'Historia padre';
    }
    
    // Set sprint info
    const sprintEl = document.getElementById('subtask-sprint');
    if (sprintEl && parentStory) {
        const sprint = sprints.find(s => s.id === parentStory.sprintId);
        sprintEl.textContent = sprint?.name || 'Sin sprint';
    }
    
    // Set reporter (use current user or task creator)
    const reporterEl = document.getElementById('subtask-reporter');
    const reporterName = currentUser?.name || 'Usuario';
    const reporterInitials = getInitials(reporterName);
    if (reporterEl) {
        reporterEl.innerHTML = `
            <span class="subtask-reporter-avatar">${reporterInitials}</span>
            <span class="subtask-reporter-name">${escapeHtml(reporterName)}</span>
        `;
    }
    
    // Set current user avatar
    const userAvatar = document.getElementById('subtask-current-user-avatar');
    if (userAvatar) {
        userAvatar.textContent = reporterInitials;
    }
    
    // Hide loading, show body
    if (loading) loading.style.display = 'none';
    if (body) body.style.display = 'grid';
}

function closeSubtaskDetail() {
    hideModal('subtask-detail-modal');
    currentSubtaskDetail = null;
}

function editSubtask() {
    if (!currentSubtaskDetail) return;
    // TODO: Implement edit functionality
    showToast('Función de edición en desarrollo', 'info');
}

async function updateSubtaskStatus(status) {
    if (!currentSubtaskDetail) return;
    
    try {
        await apiRequest(`/api/tasks/${currentSubtaskDetail.id}/status`, 'PUT', { status });
        currentSubtaskDetail.status = status;
        showToast('Estado actualizado', 'success');
        
        // Refresh parent issue detail if open
        if (currentIssueDetail) {
            renderIssueSubtasks(currentIssueDetail.subtasks || []);
        }
    } catch (error) {
        showToast('Error al actualizar estado', 'error');
    }
}

async function assignSubtaskToMe() {
    if (!currentSubtaskDetail || !currentUser) return;
    
    try {
        await apiRequest(`/api/tasks/${currentSubtaskDetail.id}/assign`, {
            method: 'PATCH',
            body: { assignedToId: currentUser.id }
        });
        currentSubtaskDetail.assignedToId = currentUser.id;
        
        // Update display
        const assigneeDisplay = document.getElementById('subtask-assignee-display');
        assigneeDisplay.innerHTML = `
            <span class="subtask-assignee-avatar" style="background: ${getUserColor(currentUser.id)} !important;">${getInitials(currentUser.name)}</span>
            <span class="subtask-assignee-name">${escapeHtml(currentUser.name)}</span>
        `;
        
        showToast('Te has asignado la subtarea', 'success');
        
        // Refresh parent issue detail if open
        if (currentIssueDetail) {
            renderIssueSubtasks(currentIssueDetail.subtasks || []);
        }
    } catch (error) {
        showToast('Error al asignar subtarea', 'error');
    }
}

async function addSubtaskComment() {
    const commentInput = document.getElementById('subtask-new-comment');
    const content = commentInput?.value?.trim();
    
    if (!content) {
        showToast('Escribe un comentario primero', 'warning');
        return;
    }
    
    if (!currentSubtaskDetail) return;
    
    try {
        // TODO: Implement comment API endpoint
        showToast('Comentario agregado', 'success');
        commentInput.value = '';
        
        // Add comment to list (temporary until API is ready)
        const commentsList = document.getElementById('subtask-comments-list');
        const commentEl = document.createElement('div');
        commentEl.className = 'subtask-comment';
        commentEl.innerHTML = `
            <div class="subtask-comment-header">
                <span class="subtask-comment-author">${escapeHtml(currentUser?.name || 'Usuario')}</span>
                <span class="subtask-comment-time">Ahora</span>
            </div>
            <div class="subtask-comment-content">${escapeHtml(content)}</div>
        `;
        commentsList.prepend(commentEl);
    } catch (error) {
        showToast('Error al agregar comentario', 'error');
    }
}

function configureSubtask() {
    showToast('Configuración en desarrollo', 'info');
}

// Close subtask modal on Escape key
document.addEventListener('keydown', function(event) {
    const modal = document.getElementById('subtask-detail-modal');
    if (event.key === 'Escape' && modal?.classList.contains('active')) {
        closeSubtaskDetail();
    }
});

// ==================== RICH TEXT EDITOR FUNCTIONS ====================
function formatSubtaskText(command, value = null) {
    const editor = document.getElementById('subtask-description');
    if (!editor) return;
    
    editor.focus();
    
    try {
        document.execCommand(command, false, value);
    } catch (e) {
        console.error('Error formatting text:', e);
    }
    
    // Remove placeholder text if present
    const placeholder = editor.querySelector('.placeholder-text');
    if (placeholder && editor.textContent.trim().length > 0) {
        placeholder.remove();
    }
}

function toggleMoreToolbarOptions() {
    const moreOptions = document.getElementById('toolbar-more-options');
    if (moreOptions) {
        moreOptions.style.display = moreOptions.style.display === 'none' ? 'flex' : 'none';
    }
}

function insertSubtaskCheckbox() {
    const editor = document.getElementById('subtask-description');
    if (!editor) return;
    
    editor.focus();
    
    const checkboxHtml = '<input type="checkbox" disabled style="margin-right: 8px; accent-color: #8b5cf6;"> ';
    document.execCommand('insertHTML', false, checkboxHtml);
}

function insertSubtaskLink() {
    const editor = document.getElementById('subtask-description');
    if (!editor) return;
    
    // Create modal for link insertion
    const modal = document.createElement('div');
    modal.className = 'subtask-link-modal';
    modal.innerHTML = `
        <div class="subtask-link-modal-content">
            <div class="subtask-link-modal-header">
                <h3><i class="fas fa-link"></i> Insertar Enlace</h3>
                <button class="subtask-link-modal-close" onclick="this.closest('.subtask-link-modal').remove()">
                    <i class="fas fa-times"></i>
                </button>
            </div>
            <div class="subtask-link-modal-body">
                <div class="form-group">
                    <label for="link-url">URL del enlace:</label>
                    <input type="url" id="link-url" placeholder="https://ejemplo.com" value="https://">
                </div>
                <div class="form-group">
                    <label for="link-text">Texto del enlace:</label>
                    <input type="text" id="link-text" placeholder="Texto que se mostrará">
                </div>
                <div class="form-group">
                    <label>
                        <input type="checkbox" id="link-new-tab" checked>
                        Abrir en nueva pestaña
                    </label>
                </div>
            </div>
            <div class="subtask-link-modal-footer">
                <button class="btn btn-secondary" onclick="this.closest('.subtask-link-modal').remove()">Cancelar</button>
                <button class="btn btn-primary" onclick="insertLinkFromModal(this)">Insertar Enlace</button>
            </div>
        </div>
    `;
    
    // Get selected text
    const selection = window.getSelection();
    const selectedText = selection.toString();
    if (selectedText) {
        modal.querySelector('#link-text').value = selectedText;
    }
    
    document.body.appendChild(modal);
    
    // Focus URL input
    setTimeout(() => {
        modal.querySelector('#link-url').focus();
        modal.querySelector('#link-url').select();
    }, 100);
    
    // Handle Enter key
    modal.querySelector('#link-url').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            insertLinkFromModal(modal.querySelector('.btn-primary'));
        }
    });
    
    modal.querySelector('#link-text').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') {
            insertLinkFromModal(modal.querySelector('.btn-primary'));
        }
    });
}

function insertLinkFromModal(button) {
    const modal = button.closest('.subtask-link-modal');
    const url = modal.querySelector('#link-url').value.trim();
    const text = modal.querySelector('#link-text').value.trim() || url;
    const newTab = modal.querySelector('#link-new-tab').checked;
    
    if (!url || url === 'https://') {
        modal.querySelector('#link-url').focus();
        return;
    }
    
    // Validate URL
    try {
        new URL(url);
    } catch {
        alert('Por favor, ingresa una URL válida.');
        modal.querySelector('#link-url').focus();
        return;
    }
    
    const editor = document.getElementById('subtask-description');
    const target = newTab ? ' target="_blank" rel="noopener noreferrer"' : '';
    const linkHtml = `<a href="${escapeHtml(url)}"${target}>${escapeHtml(text)}</a>`;
    
    document.execCommand('insertHTML', false, linkHtml);
    modal.remove();
    
    // Trigger auto-save
    if (window.autoSaveTimeout) {
        clearTimeout(window.autoSaveTimeout);
    }
    // Save only when save button is clicked
}

function insertSubtaskImage() {
    const editor = document.getElementById('subtask-description');
    if (!editor) return;
    
    // Create modal for file upload
    const modal = document.createElement('div');
    modal.className = 'subtask-image-modal';
    modal.innerHTML = `
        <div class="subtask-image-modal-content">
            <div class="subtask-image-modal-header">
                <h3><i class="fas fa-image"></i> Insertar Archivo/Imagen</h3>
                <button class="subtask-image-modal-close" onclick="this.closest('.subtask-image-modal').remove()">
                    <i class="fas fa-times"></i>
                </button>
            </div>
            <div class="subtask-image-modal-body">
                <div class="form-group">
                    <label for="file-upload">Seleccionar archivo:</label>
                    <div class="file-upload-area" id="file-upload-area">
                        <input type="file" id="file-upload" accept="image/*,.xlsx,.xls,.doc,.docx,.pdf,.txt,.csv" style="display: none;">
                        <div class="file-upload-dropzone" id="file-dropzone">
                            <i class="fas fa-cloud-upload-alt"></i>
                            <p>Arrastra un archivo aquí o haz click para seleccionar</p>
                            <small>Imágenes, Excel, Word, PDF, TXT, CSV</small>
                        </div>
                        <div class="file-preview" id="file-preview" style="display: none;">
                            <div class="file-preview-content">
                                <div class="file-preview-icon" id="file-preview-icon"></div>
                                <div class="file-preview-info">
                                    <div class="file-name" id="file-name"></div>
                                    <div class="file-size" id="file-size"></div>
                                </div>
                                <button class="file-remove" onclick="removeSelectedFile()" title="Quitar archivo">
                                    <i class="fas fa-times"></i>
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
                <div class="form-group">
                    <label for="file-alt">Texto alternativo/descripción:</label>
                    <input type="text" id="file-alt" placeholder="Descripción del archivo (opcional)">
                </div>
                <div class="form-group">
                    <label for="file-size-option">Tamaño (solo para imágenes):</label>
                    <select id="file-size-option">
                        <option value="small">Pequeña</option>
                        <option value="medium" selected>Mediana</option>
                        <option value="large">Grande</option>
                        <option value="original">Tamaño original</option>
                    </select>
                </div>
                <div class="form-group">
                    <label for="file-align">Alineación:</label>
                    <select id="file-align">
                        <option value="none" selected>Ninguna</option>
                        <option value="left">Izquierda</option>
                        <option value="center">Centro</option>
                        <option value="right">Derecha</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>
                        <input type="checkbox" id="file-border" checked>
                        Agregar borde (solo para imágenes)
                    </label>
                </div>
            </div>
            <div class="subtask-image-modal-footer">
                <button class="btn btn-secondary" onclick="this.closest('.subtask-image-modal').remove()">Cancelar</button>
                <button class="btn btn-primary" onclick="insertFileFromModal(this)" id="insert-file-btn" disabled>Insertar Archivo</button>
            </div>
        </div>
    `;
    
    document.body.appendChild(modal);
    
    // Setup file upload handlers
    const fileInput = modal.querySelector('#file-upload');
    const dropzone = modal.querySelector('#file-dropzone');
    const uploadArea = modal.querySelector('#file-upload-area');
    
    // Click to select file
    dropzone.addEventListener('click', () => fileInput.click());
    
    // File selection
    fileInput.addEventListener('change', handleFileSelection);
    
    // Drag and drop
    uploadArea.addEventListener('dragover', (e) => {
        e.preventDefault();
        uploadArea.classList.add('drag-over');
    });
    
    uploadArea.addEventListener('dragleave', () => {
        uploadArea.classList.remove('drag-over');
    });
    
    uploadArea.addEventListener('drop', (e) => {
        e.preventDefault();
        uploadArea.classList.remove('drag-over');
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            fileInput.files = files;
            handleFileSelection({ target: fileInput });
        }
    });
}

function handleFileSelection(event) {
    const file = event.target.files[0];
    if (!file) return;
    
    const modal = document.querySelector('.subtask-image-modal');
    const dropzone = modal.querySelector('#file-dropzone');
    const preview = modal.querySelector('#file-preview');
    const fileName = modal.querySelector('#file-name');
    const fileSize = modal.querySelector('#file-size');
    const fileIcon = modal.querySelector('#file-preview-icon');
    const insertBtn = modal.querySelector('#insert-file-btn');
    
    // Show file info
    fileName.textContent = file.name;
    fileSize.textContent = formatFileSize(file.size);
    
    // Set appropriate icon
    const icon = getFileIcon(file.type);
    fileIcon.innerHTML = `<i class="fas ${icon}"></i>`;
    
    // If it's an image, show preview
    if (file.type.startsWith('image/')) {
        const reader = new FileReader();
        reader.onload = (e) => {
            fileIcon.innerHTML = `<img src="${e.target.result}" alt="Preview" style="width: 40px; height: 40px; object-fit: cover; border-radius: 4px;">`;
        };
        reader.readAsDataURL(file);
    }
    
    // Show preview, hide dropzone
    dropzone.style.display = 'none';
    preview.style.display = 'block';
    
    // Enable insert button
    insertBtn.disabled = false;
}

function removeSelectedFile() {
    const modal = document.querySelector('.subtask-image-modal');
    const fileInput = modal.querySelector('#file-upload');
    const dropzone = modal.querySelector('#file-dropzone');
    const preview = modal.querySelector('#file-preview');
    const insertBtn = modal.querySelector('#insert-file-btn');
    
    // Reset file input
    fileInput.value = '';
    
    // Show dropzone, hide preview
    dropzone.style.display = 'block';
    preview.style.display = 'none';
    
    // Disable insert button
    insertBtn.disabled = true;
}

function formatFileSize(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function getFileIcon(fileType) {
    if (fileType.startsWith('image/')) return 'fa-image';
    if (fileType.includes('excel') || fileType.includes('spreadsheet')) return 'fa-file-excel';
    if (fileType.includes('word') || fileType.includes('document')) return 'fa-file-word';
    if (fileType.includes('pdf')) return 'fa-file-pdf';
    if (fileType.includes('text')) return 'fa-file-alt';
    if (fileType.includes('csv')) return 'fa-file-csv';
    return 'fa-file';
}

function insertFileFromModal(button) {
    const modal = button.closest('.subtask-image-modal');
    const fileInput = modal.querySelector('#file-upload');
    const file = fileInput.files[0];
    
    if (!file) {
        alert('Por favor, selecciona un archivo.');
        return;
    }
    
    const alt = modal.querySelector('#file-alt').value.trim() || file.name;
    const size = modal.querySelector('#file-size-option').value;
    const align = modal.querySelector('#file-align').value;
    const border = modal.querySelector('#file-border').checked;
    
    // Read file as data URL
    const reader = new FileReader();
    reader.onload = (e) => {
        const dataUrl = e.target.result;
        
        if (file.type.startsWith('image/')) {
            // Handle image file
            insertImageFromFile(dataUrl, alt, size, align, border);
        } else {
            // Handle other file types
            insertFileAsLink(file, dataUrl, alt);
        }
        
        modal.remove();
        
        // Save only when save button is clicked
    };
    
    reader.readAsDataURL(file);
}

function insertImageFromFile(dataUrl, alt, size, align, border) {
    const editor = document.getElementById('subtask-description');
    if (!editor) {
        console.error('Editor not found');
        return;
    }
    
    console.log('Inserting image:', { dataUrl: dataUrl.substring(0, 50) + '...', alt, size, align, border });
    
    // Remove placeholder text if exists
    const placeholder = editor.querySelector('.placeholder-text');
    if (placeholder) {
        placeholder.remove();
    }
    
    // Clear editor if it only has placeholder
    if (editor.textContent.trim() === 'Agrega una descripción más detallada...' || 
        editor.innerHTML.trim() === '<p class="placeholder-text">Agrega una descripción más detallada...</p>') {
        editor.innerHTML = '';
    }
    
    // Build style based on options
    let style = 'max-width: 100%; border-radius: 4px; display: inline-block; visibility: visible !important; opacity: 1 !important;';
    
    switch (size) {
        case 'small':
            style += ' width: 200px; height: auto;';
            break;
        case 'medium':
            style += ' width: 400px; height: auto;';
            break;
        case 'large':
            style += ' width: 600px; height: auto;';
            break;
        case 'original':
            style += ' width: auto; height: auto;';
            break;
    }
    const imageId = 'img-' + Date.now() + '-' + Math.random().toString(36).substr(2, 9);
    
    // Create image element directly
    const img = document.createElement('img');
    img.src = dataUrl;
    img.alt = alt;
    img.style.maxWidth = '100%';
    img.style.height = 'auto';
    img.style.borderRadius = '8px';
    img.style.cursor = 'pointer';
    img.setAttribute('data-image-id', imageId);
    
    // Create wrapper div with proper container styling
    const wrapper = document.createElement('div');
    wrapper.style.margin = '8px 0';
    wrapper.style.position = 'relative';
    wrapper.style.display = 'inline-block';
    wrapper.style.border = '1px solid rgba(139, 92, 246, 0.1)';
    wrapper.style.borderRadius = '8px';
    wrapper.style.padding = '4px';
    wrapper.style.background = 'transparent';
    wrapper.style.clear = 'both';
    wrapper.contentEditable = 'false';
    wrapper.setAttribute('data-image-id', imageId);
    wrapper.className = 'simple-image-wrapper';
    
    // Create controls container
    const controls = document.createElement('div');
    controls.style.position = 'absolute';
    controls.style.top = '8px';
    controls.style.right = '8px';
    controls.style.display = 'flex';
    controls.style.gap = '4px';
    controls.style.opacity = '0';
    controls.style.transition = 'opacity 0.2s ease';
    controls.className = 'simple-image-controls';
    
    // Create view button
    const viewBtn = document.createElement('button');
    viewBtn.innerHTML = '<i class="fas fa-expand"></i>';
    viewBtn.style.background = 'rgba(0,0,0,0.7)';
    viewBtn.style.color = 'white';
    viewBtn.style.border = 'none';
    viewBtn.style.borderRadius = '4px';
    viewBtn.style.padding = '6px';
    viewBtn.style.cursor = 'pointer';
    viewBtn.style.fontSize = '12px';
    viewBtn.title = 'Ver imagen';
    viewBtn.onclick = () => viewImageModal(dataUrl, alt);
    
    // Create delete button
    const deleteBtn = document.createElement('button');
    deleteBtn.innerHTML = '<i class="fas fa-trash"></i>';
    deleteBtn.style.background = 'rgba(239,68,68,0.9)';
    deleteBtn.style.color = 'white';
    deleteBtn.style.border = 'none';
    deleteBtn.style.borderRadius = '4px';
    deleteBtn.style.padding = '6px';
    deleteBtn.style.cursor = 'pointer';
    deleteBtn.style.fontSize = '12px';
    deleteBtn.title = 'Eliminar imagen';
    deleteBtn.onclick = () => removeSimpleImage(imageId);
    
    // Assemble the components
    controls.appendChild(viewBtn);
    controls.appendChild(deleteBtn);
    wrapper.appendChild(img);
    wrapper.appendChild(controls);
    
    // Prevent text editing and cursor placement around image
    wrapper.addEventListener('click', (e) => {
        e.stopPropagation();
        e.preventDefault();
    });
    
    wrapper.addEventListener('mousedown', (e) => {
        e.stopPropagation();
        e.preventDefault();
    });
    
    wrapper.addEventListener('keydown', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    
    wrapper.addEventListener('input', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    
    // Show controls on hover
    wrapper.addEventListener('mouseenter', () => {
        controls.style.opacity = '1';
    });
    
    wrapper.addEventListener('mouseleave', () => {
        controls.style.opacity = '0';
    });
    
    // Image click handler - no toolbar, just prevent default behavior
    img.onclick = (e) => {
        e.stopPropagation();
        e.preventDefault();
    };
    
    // Prevent any text editing around the image
    wrapper.addEventListener('selectstart', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    
    wrapper.addEventListener('dblclick', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    
    // Focus editor and insert at cursor position
    editor.focus();
    
    try {
        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            
            // Delete any selected content
            range.deleteContents();
            
            // Insert the wrapper at cursor position
            range.insertNode(wrapper);
            
            // Add a line break after the image to ensure separation
            const lineBreak = document.createElement('br');
            range.insertNode(lineBreak);
            
            // Move cursor after the line break
            range.setStartAfter(lineBreak);
            range.collapse(true);
            selection.removeAllRanges();
            selection.addRange(range);
            
            console.log('✅ Simple image inserted at cursor position');
        } else {
            // Fallback: append at end
            editor.appendChild(wrapper);
            console.log('Simple image appended at end');
        }
    } catch (e) {
        console.error('Simple image insertion failed:', e);
        // Fallback: append at end
        editor.appendChild(wrapper);
    }
    
    // Setup mutation observer for image controls
    setupImageMutationObserver();
}

function removeSimpleImage(imageId) {
    if (confirm('¿Estás seguro de que quieres eliminar esta imagen?')) {
        const wrapper = document.querySelector(`[data-image-id="${imageId}"]`);
        if (wrapper) {
            wrapper.remove();
            
            // Trigger auto-save
            if (window.autoSaveTimeout) {
                clearTimeout(window.autoSaveTimeout);
            }
            // Save only when save button is clicked
            showToast('Imagen eliminada', 'success');
        }
    }
}

// Setup MutationObserver to handle moved images
function setupImageMutationObserver() {
    const editor = document.getElementById('subtask-description');
    if (!editor) return;
    
    // Check if observer already exists
    if (editor.hasAttribute('data-mutation-observer-setup')) {
        return;
    }
    
    editor.setAttribute('data-mutation-observer-setup', 'true');
    
    const observer = new MutationObserver((mutations) => {
        mutations.forEach((mutation) => {
            if (mutation.type === 'childList') {
                // Check for moved or new image wrappers
                mutation.addedNodes.forEach((node) => {
                    if (node.nodeType === 1) { // Element node
                        const imageWrappers = node.querySelectorAll ? node.querySelectorAll('.simple-image-wrapper') : [];
                        const isImageWrapper = node.classList && node.classList.contains('simple-image-wrapper');
                        
                        // Handle the node itself if it's an image wrapper
                        if (isImageWrapper) {
                            setupImageControls(node);
                        }
                        
                        // Handle any image wrappers within the added nodes
                        imageWrappers.forEach(setupImageControls);
                    }
                });
            }
        });
    });
    
    // Start observing
    observer.observe(editor, {
        childList: true,
        subtree: true
    });
    
    // Setup click outside handler
    document.addEventListener('click', (e) => {
        if (!e.target.closest('.simple-image-wrapper') && !e.target.closest('.image-selection-toolbar')) {
            hideImageSelectionToolbar();
        }
    });
    
    console.log('MutationObserver setup for image controls');
}

// Setup controls for a single image wrapper
// Show image selection toolbar
function showImageSelectionToolbar(wrapper, dataUrl, alt, imageId) {
    // Remove existing toolbar
    hideImageSelectionToolbar();
    
    // Create toolbar
    const toolbar = document.createElement('div');
    toolbar.className = 'image-selection-toolbar';
    toolbar.style.cssText = `
        position: absolute;
        top: -40px;
        left: 50%;
        transform: translateX(-50%);
        background: rgba(0, 0, 0, 0.9);
        color: white;
        border-radius: 8px;
        padding: 8px 12px;
        display: flex;
        gap: 12px;
        align-items: center;
        z-index: 1000;
        box-shadow: 0 4px 12px rgba(0,0,0,0.3);
        font-size: 14px;
    `;
    
    // Copy button
    const copyBtn = document.createElement('button');
    copyBtn.innerHTML = '<i class="fas fa-copy"></i> Copiar';
    copyBtn.style.cssText = `
        background: transparent;
        border: none;
        color: white;
        cursor: pointer;
        padding: 4px 8px;
        border-radius: 4px;
        font-size: 12px;
        display: flex;
        align-items: center;
        gap: 6px;
    `;
    copyBtn.onmouseover = () => copyBtn.style.background = 'rgba(255,255,255,0.2)';
    copyBtn.onmouseout = () => copyBtn.style.background = 'transparent';
    copyBtn.onclick = (e) => {
        e.stopPropagation();
        copyImageToClipboard(dataUrl);
        hideImageSelectionToolbar();
    };
    
    // Delete button
    const deleteBtn = document.createElement('button');
    deleteBtn.innerHTML = '<i class="fas fa-trash"></i> Eliminar';
    deleteBtn.style.cssText = `
        background: rgba(239,68,68,0.8);
        border: none;
        color: white;
        cursor: pointer;
        padding: 4px 8px;
        border-radius: 4px;
        font-size: 12px;
        display: flex;
        align-items: center;
        gap: 6px;
    `;
    deleteBtn.onmouseover = () => deleteBtn.style.background = 'rgba(239,68,68,1)';
    deleteBtn.onmouseout = () => deleteBtn.style.background = 'rgba(239,68,68,0.8)';
    deleteBtn.onclick = (e) => {
        e.stopPropagation();
        removeSimpleImage(imageId);
        hideImageSelectionToolbar();
    };
    
    // Close button
    const closeBtn = document.createElement('button');
    closeBtn.innerHTML = '<i class="fas fa-times"></i>';
    closeBtn.style.cssText = `
        background: transparent;
        border: none;
        color: white;
        cursor: pointer;
        padding: 4px;
        border-radius: 4px;
        font-size: 12px;
        margin-left: auto;
    `;
    closeBtn.onmouseover = () => closeBtn.style.background = 'rgba(255,255,255,0.2)';
    closeBtn.onmouseout = () => closeBtn.style.background = 'transparent';
    closeBtn.onclick = (e) => {
        e.stopPropagation();
        hideImageSelectionToolbar();
    };
    
    // Assemble toolbar
    toolbar.appendChild(copyBtn);
    toolbar.appendChild(deleteBtn);
    toolbar.appendChild(closeBtn);
    
    // Position toolbar relative to image
    const img = wrapper.querySelector('img');
    if (img) {
        const rect = img.getBoundingClientRect();
        const editorRect = wrapper.parentElement.getBoundingClientRect();
        
        toolbar.style.position = 'fixed';
        toolbar.style.top = (rect.top - 50) + 'px';
        toolbar.style.left = (rect.left + rect.width / 2 - 100) + 'px'; // Center horizontally
    }
    
    // Add to body
    document.body.appendChild(toolbar);
    
    // Store reference
    window.currentImageToolbar = toolbar;
    
    // Auto-hide after 5 seconds
    setTimeout(hideImageSelectionToolbar, 5000);
}

// Hide image selection toolbar
function hideImageSelectionToolbar() {
    if (window.currentImageToolbar) {
        window.currentImageToolbar.remove();
        window.currentImageToolbar = null;
    }
}

// Copy image to clipboard
function copyImageToClipboard(dataUrl) {
    try {
        // Convert dataUrl to blob
        const response = fetch(dataUrl);
        response.then(res => res.blob()).then(blob => {
            const item = new ClipboardItem({ 'image/png': blob });
            navigator.clipboard.write([item]).then(() => {
                showToast('Imagen copiada al portapapeles', 'success');
            }).catch(err => {
                console.error('Failed to copy image:', err);
                showToast('Error al copiar imagen', 'error');
            });
        });
    } catch (error) {
        console.error('Clipboard API not available:', error);
        // Fallback: copy dataUrl as text
        navigator.clipboard.writeText(dataUrl).then(() => {
            showToast('URL de imagen copiada', 'success');
        }).catch(err => {
            console.error('Failed to copy URL:', err);
            showToast('Error al copiar URL', 'error');
        });
    }
}

function setupImageControls(wrapper) {
    // Skip if already has event listeners
    if (wrapper.hasAttribute('data-controls-setup')) {
        return;
    }
    
    wrapper.setAttribute('data-controls-setup', 'true');
    
    const img = wrapper.querySelector('img');
    const controls = wrapper.querySelector('.simple-image-controls');
    
    if (!img || !controls) return;
    
    // Remove existing listeners to avoid duplicates
    const newImg = img.cloneNode(true);
    const newControls = controls.cloneNode(true);
    
    // Replace old elements
    img.parentNode.replaceChild(newImg, img);
    controls.parentNode.replaceChild(newControls, controls);
    
    // Re-attach event listeners
    newImg.onclick = (e) => {
        e.stopPropagation();
        const imageUrl = newImg.src;
        const imageAlt = newImg.alt;
        const imageId = wrapper.getAttribute('data-image-id');
        showImageSelectionToolbar(wrapper, imageUrl, imageAlt, imageId);
    };
    
    wrapper.addEventListener('mouseenter', () => {
        newControls.style.opacity = '1';
    });
    
    wrapper.addEventListener('mouseleave', () => {
        newControls.style.opacity = '0';
    });
    
    // Setup button handlers
    const viewBtn = newControls.querySelector('button:first-child');
    const deleteBtn = newControls.querySelector('button:last-child');
    
    if (viewBtn) {
        viewBtn.onclick = (e) => {
            e.stopPropagation();
            const imageUrl = newImg.src;
            const imageAlt = newImg.alt;
            viewImageModal(imageUrl, imageAlt);
        };
    }
    
    if (deleteBtn) {
        const imageId = wrapper.getAttribute('data-image-id');
        deleteBtn.onclick = (e) => {
            e.stopPropagation();
            removeSimpleImage(imageId);
        };
    }
}

function insertFileAsLink(file, dataUrl, alt) {
    const editor = document.getElementById('subtask-description');
    if (!editor) {
        console.error('Editor not found');
        return;
    }
    
    // Remove placeholder text if exists
    const placeholder = editor.querySelector('.placeholder-text');
    if (placeholder) {
        placeholder.remove();
    }
    
    // Clear editor if it only has placeholder
    if (editor.textContent.trim() === 'Agrega una descripción más detallada...' || 
        editor.innerHTML.trim() === '<p class="placeholder-text">Agrega una descripción más detallada...</p>') {
        editor.innerHTML = '';
    }
    
    // Generate unique document ID
    const documentId = 'doc-' + Date.now() + '-' + Math.random().toString(36).substr(2, 9);
    
    // Create document icon
    const icon = getFileIcon(file.type);
    
    // Create document element
    const docElement = document.createElement('div');
    docElement.style.display = 'inline-flex';
    docElement.style.alignItems = 'center';
    docElement.style.gap = '8px';
    docElement.style.padding = '6px 10px';
    docElement.style.background = 'rgba(139, 92, 246, 0.05)';
    docElement.style.border = '1px solid rgba(139, 92, 246, 0.1)';
    docElement.style.borderRadius = '8px';
    docElement.style.margin = '6px 0';
    docElement.style.textDecoration = 'none';
    docElement.style.color = 'var(--text-primary)';
    docElement.style.position = 'relative';
    docElement.style.clear = 'both';
    docElement.style.cursor = 'pointer';
    docElement.contentEditable = 'false';
    docElement.setAttribute('data-document-id', documentId);
    docElement.setAttribute('data-download-url', dataUrl);
    docElement.className = 'simple-document-wrapper';
    
    // Create icon
    const iconElement = document.createElement('i');
    iconElement.className = 'fas ' + icon;
    iconElement.style.color = 'var(--primary-purple)';
    iconElement.style.fontSize = '16px';
    
    // Create file info container
    const fileInfo = document.createElement('div');
    fileInfo.style.display = 'flex';
    fileInfo.style.flexDirection = 'column';
    fileInfo.style.gap = '2px';
    
    // Create file name
    const fileName = document.createElement('span');
    fileName.style.fontWeight = '500';
    fileName.style.fontSize = '13px';
    fileName.style.color = 'var(--text-primary)';
    fileName.textContent = escapeHtml(file.name);
    
    // Create file size
    const fileSize = document.createElement('span');
    fileSize.style.fontSize = '11px';
    fileSize.style.color = 'var(--text-muted)';
    fileSize.textContent = formatFileSize(file.size);
    
    // Assemble file info
    fileInfo.appendChild(fileName);
    fileInfo.appendChild(fileSize);
    
    // Create controls container
    const controls = document.createElement('div');
    controls.style.display = 'flex';
    controls.style.gap = '4px';
    controls.style.opacity = '0';
    controls.style.transition = 'opacity 0.2s ease';
    controls.style.alignItems = 'center';
    
    // Create download button
    const downloadBtn = document.createElement('button');
    downloadBtn.innerHTML = '<i class="fas fa-download"></i>';
    downloadBtn.style.background = 'rgba(59, 130, 246, 0.8)';
    downloadBtn.style.color = 'white';
    downloadBtn.style.border = 'none';
    downloadBtn.style.borderRadius = '50%';
    downloadBtn.style.width = '20px';
    downloadBtn.style.height = '20px';
    downloadBtn.style.padding = '0';
    downloadBtn.style.cursor = 'pointer';
    downloadBtn.style.fontSize = '10px';
    downloadBtn.style.display = 'flex';
    downloadBtn.style.alignItems = 'center';
    downloadBtn.style.justifyContent = 'center';
    downloadBtn.title = 'Descargar documento';
    downloadBtn.onclick = (e) => {
        e.stopPropagation();
        const downloadUrl = docElement.getAttribute('data-download-url');
        if (downloadUrl) {
            const link = document.createElement('a');
            link.href = downloadUrl;
            link.download = file.name;
            link.style.display = 'none';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
        }
    };
    
    // Create delete button
    const deleteBtn = document.createElement('button');
    deleteBtn.innerHTML = '<i class="fas fa-times"></i>';
    deleteBtn.style.background = 'rgba(239,68,68,0.8)';
    deleteBtn.style.color = 'white';
    deleteBtn.style.border = 'none';
    deleteBtn.style.borderRadius = '50%';
    deleteBtn.style.width = '20px';
    deleteBtn.style.height = '20px';
    deleteBtn.style.padding = '0';
    deleteBtn.style.cursor = 'pointer';
    deleteBtn.style.fontSize = '10px';
    deleteBtn.style.display = 'flex';
    deleteBtn.style.alignItems = 'center';
    deleteBtn.style.justifyContent = 'center';
    deleteBtn.title = 'Eliminar documento';
    deleteBtn.onclick = (e) => {
        e.stopPropagation();
        removeSimpleDocument(documentId);
    };
    
    // Assemble controls
    controls.appendChild(downloadBtn);
    controls.appendChild(deleteBtn);
    
    // Assemble the components
    docElement.appendChild(iconElement);
    docElement.appendChild(fileInfo);
    docElement.appendChild(controls);
    
    // Click to download functionality
    docElement.addEventListener('click', (e) => {
        e.stopPropagation();
        e.preventDefault();
        const downloadUrl = docElement.getAttribute('data-download-url');
        if (downloadUrl) {
            // Create download link
            const link = document.createElement('a');
            link.href = downloadUrl;
            link.download = file.name;
            link.style.display = 'none';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
        }
    });
    
    // Prevent text editing but allow download
    docElement.addEventListener('mousedown', (e) => {
        e.stopPropagation();
    });
    
    docElement.addEventListener('keydown', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    
    docElement.addEventListener('input', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    
    docElement.addEventListener('selectstart', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    
    docElement.addEventListener('dblclick', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    
    // Show controls on hover
    docElement.addEventListener('mouseenter', () => {
        controls.style.opacity = '1';
        docElement.style.background = 'rgba(139, 92, 246, 0.08)';
        docElement.style.borderColor = 'rgba(139, 92, 246, 0.2)';
    });
    
    docElement.addEventListener('mouseleave', () => {
        controls.style.opacity = '0';
        docElement.style.background = 'rgba(139, 92, 246, 0.05)';
        docElement.style.borderColor = 'rgba(139, 92, 246, 0.1)';
    });
    
    // Focus editor and insert at cursor position
    editor.focus();
    
    try {
        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            
            // Delete any selected content
            range.deleteContents();
            
            // Insert the document element
            range.insertNode(docElement);
            
            // Add line break after document
            const lineBreak = document.createElement('br');
            range.insertNode(lineBreak);
            range.setStartAfter(lineBreak);
            range.collapse(true);
            selection.removeAllRanges();
            selection.addRange(range);
        } else {
            // Fallback: insert at the end
            editor.appendChild(docElement);
            const lineBreak = document.createElement('br');
            editor.appendChild(lineBreak);
        }
    } catch (error) {
        console.error('Document insertion failed:', error);
        // Fallback: append to editor
        editor.appendChild(docElement);
        const lineBreak = document.createElement('br');
        editor.appendChild(lineBreak);
    }
    
    // Initialize mutation observer for documents
    initializeDocumentObserver();
}

function removeSimpleDocument(documentId) {
    if (confirm('¿Estás seguro de que quieres eliminar este documento?')) {
        const wrapper = document.querySelector(`[data-document-id="${documentId}"]`);
        if (wrapper) {
            wrapper.remove();
            showToast('Imagen eliminada', 'success');
        }
    }
}

function initializeDocumentObserver() {
    if (window.documentObserverInitialized) return;
    
    const editor = document.getElementById('subtask-description');
    if (!editor) return;
    
    const observer = new MutationObserver((mutations) => {
        mutations.forEach((mutation) => {
            mutation.addedNodes.forEach((node) => {
                if (node.nodeType === Node.ELEMENT_NODE) {
                    // Check if this is a document wrapper that was moved
                    const docWrappers = node.querySelectorAll ? node.querySelectorAll('.simple-document-wrapper') : [];
                    docWrappers.forEach(wrapper => {
                        // Re-attach event listeners if needed
                        if (!wrapper.hasAttribute('data-listeners-attached')) {
                            attachDocumentEventListeners(wrapper);
                            wrapper.setAttribute('data-listeners-attached', 'true');
                        }
                    });
                    
                    // Also check if the node itself is a document wrapper
                    if (node.classList && node.classList.contains('simple-document-wrapper')) {
                        if (!node.hasAttribute('data-listeners-attached')) {
                            attachDocumentEventListeners(node);
                            node.setAttribute('data-listeners-attached', 'true');
                        }
                    }
                }
            });
        });
    });
    
    observer.observe(editor, {
        childList: true,
        subtree: true
    });
    
    window.documentObserverInitialized = true;
}

function attachDocumentEventListeners(wrapper) {
    const controls = wrapper.querySelector('div:last-child');
    const downloadBtn = controls.querySelector('button:first-child');
    const deleteBtn = controls.querySelector('button:last-child');
    
    if (downloadBtn) {
        const documentId = wrapper.getAttribute('data-document-id');
        downloadBtn.onclick = (e) => {
            e.stopPropagation();
            const downloadUrl = wrapper.getAttribute('data-download-url');
            if (downloadUrl) {
                const link = document.createElement('a');
                link.href = downloadUrl;
                link.download = wrapper.querySelector('span').textContent;
                link.style.display = 'none';
                document.body.appendChild(link);
                link.click();
                document.body.removeChild(link);
            }
        };
    }
    
    if (deleteBtn) {
        const documentId = wrapper.getAttribute('data-document-id');
        deleteBtn.onclick = (e) => {
            e.stopPropagation();
            removeSimpleDocument(documentId);
        };
    }
    
    // Re-attach click to download functionality
    wrapper.addEventListener('click', (e) => {
        if (e.target === downloadBtn || e.target === deleteBtn) return;
        
        e.stopPropagation();
        e.preventDefault();
        const downloadUrl = wrapper.getAttribute('data-download-url');
        if (downloadUrl) {
            const link = document.createElement('a');
            link.href = downloadUrl;
            link.download = wrapper.querySelector('span').textContent;
            link.style.display = 'none';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
        }
    });
    
    // Re-attach hover effects
    wrapper.addEventListener('mouseenter', () => {
        if (controls) controls.style.opacity = '1';
        wrapper.style.background = 'rgba(139, 92, 246, 0.08)';
        wrapper.style.borderColor = 'rgba(139, 92, 246, 0.2)';
    });
    
    wrapper.addEventListener('mouseleave', () => {
        if (controls) controls.style.opacity = '0';
        wrapper.style.background = 'rgba(139, 92, 246, 0.05)';
        wrapper.style.borderColor = 'rgba(139, 92, 246, 0.1)';
    });
}

function insertSubtaskMention() {
    const editor = document.getElementById('subtask-description');
    if (!editor) return;
    
    editor.focus();
    
    // Create a dropdown for member selection
    let dropdown = document.getElementById('mention-dropdown');
    if (dropdown) {
        dropdown.remove();
        return;
    }
    
    dropdown = document.createElement('div');
    dropdown.id = 'mention-dropdown';
    dropdown.className = 'emoji-picker-dropdown';
    dropdown.style.position = 'absolute';
    
    // Get editor position
    const rect = editor.getBoundingClientRect();
    dropdown.style.top = (rect.top + 100) + 'px';
    dropdown.style.left = rect.left + 'px';
    
    // Add members
    boardMembers.forEach(member => {
        const item = document.createElement('div');
        item.className = 'subtask-comment';
        item.style.cursor = 'pointer';
        item.innerHTML = `
            <span class="subtask-assignee-avatar" style="background: ${getUserColor(member.id)} !important; width: 24px; height: 24px; font-size: 10px;">${getInitials(member.name)}</span>
            <span style="margin-left: 8px;">${escapeHtml(member.name)}</span>
        `;
        item.onclick = () => {
            const mentionHtml = `<span class="subtask-mention">@${escapeHtml(member.name)}</span>&nbsp;`;
            document.execCommand('insertHTML', false, mentionHtml);
            dropdown.remove();
        };
        dropdown.appendChild(item);
    });
    
    document.body.appendChild(dropdown);
    
    // Close on click outside
    setTimeout(() => {
        document.addEventListener('click', function closeDropdown(e) {
            if (!dropdown.contains(e.target)) {
                dropdown.remove();
                document.removeEventListener('click', closeDropdown);
            }
        }, { once: true });
    }, 100);
}

function insertSubtaskEmoji() {
    const editor = document.getElementById('subtask-description');
    if (!editor) return;
    
    editor.focus();
    
    // Common emojis
    const emojis = ['😀', '😂', '🤔', '👍', '👎', '❤️', '🎉', '🔥', '✅', '❌', '⚠️', '💡', '📌', '🔗', '📎', '📝', '✏️', '🔍', '⚙️', '📊'];
    
    let dropdown = document.getElementById('emoji-dropdown');
    if (dropdown) {
        dropdown.remove();
        return;
    }
    
    dropdown = document.createElement('div');
    dropdown.id = 'emoji-dropdown';
    dropdown.className = 'emoji-picker-dropdown';
    dropdown.style.position = 'absolute';
    
    const rect = editor.getBoundingClientRect();
    dropdown.style.top = (rect.top + 100) + 'px';
    dropdown.style.left = rect.left + 'px';
    dropdown.style.display = 'grid';
    dropdown.style.gridTemplateColumns = 'repeat(5, 1fr)';
    dropdown.style.gap = '8px';
    
    emojis.forEach(emoji => {
        const btn = document.createElement('button');
        btn.textContent = emoji;
        btn.style.fontSize = '20px';
        btn.style.background = 'transparent';
        btn.style.border = 'none';
        btn.style.cursor = 'pointer';
        btn.style.padding = '4px';
        btn.onclick = () => {
            document.execCommand('insertText', false, emoji);
            dropdown.remove();
        };
        dropdown.appendChild(btn);
    });
    
    document.body.appendChild(dropdown);
    
    setTimeout(() => {
        document.addEventListener('click', function closeDropdown(e) {
            if (!dropdown.contains(e.target)) {
                dropdown.remove();
                document.removeEventListener('click', closeDropdown);
            }
        }, { once: true });
    }, 100);
}

async function saveSubtaskDescription() {
    const editor = document.getElementById('subtask-description');
    if (!editor || !currentSubtaskDetail) {
        console.error('Cannot save description: currentSubtaskDetail is null');
        return;
    }
    
    let description = editor.innerHTML;
    
    // Clean and validate the content
    description = cleanDescriptionContent(description);
    
    // Check if description is too large (data URLs can be very large)
    const descriptionSize = new Blob([description]).size;
    const maxSizeMB = 5; // 5MB limit
    const maxSizeBytes = maxSizeMB * 1024 * 1024;
    
    if (descriptionSize > maxSizeBytes) {
        console.error(`Description too large: ${descriptionSize} bytes (max: ${maxSizeBytes} bytes)`);
        
        // Try to reduce image sizes
        description = optimizeDescriptionForStorage(description);
        
        // Check again after optimization
        const optimizedSize = new Blob([description]).size;
        if (optimizedSize > maxSizeBytes) {
            showToast('La descripción es demasiado grande. Reduce el tamaño de las imágenes.', 'error');
            return;
        }
        
        console.log(`Optimized description size: ${optimizedSize} bytes`);
    }
    
    try {
        console.log('Saving description, size:', new Blob([description]).size, 'bytes');
        const response = await apiRequest(`/api/tasks/${currentSubtaskDetail.id}/description`, { 
            method: 'PATCH', 
            body: { description },
            headers: {
                'Content-Type': 'application/json'
            }
        });
        
        currentSubtaskDetail.description = description;
        showToast('Descripción guardada', 'success');
    } catch (error) {
        console.error('Error saving description:', error);
        
        if (error.status === 500) {
            showToast('Error del servidor al guardar. Las imágenes pueden ser demasiado grandes.', 'error');
        } else if (error.status === 413) {
            showToast('La descripción es demasiado grande. Usa imágenes más pequeñas.', 'error');
        } else {
            showToast('Error al guardar descripción: ' + (error.message || 'Error desconocido'), 'error');
        }
    }
}

function viewImageModal(dataUrl, alt) {
    // Create modal overlay
    const modal = document.createElement('div');
    modal.style.cssText = `
        position: fixed;
        top: 0;
        left: 0;
        width: 100%;
        height: 100%;
        background: rgba(0, 0, 0, 0.9);
        display: flex;
        justify-content: center;
        align-items: center;
        z-index: 10000;
        cursor: pointer;
    `;
    
    // Create image container
    const imgContainer = document.createElement('div');
    imgContainer.style.cssText = `
        max-width: 90%;
        max-height: 90%;
        position: relative;
        background: white;
        border-radius: 8px;
        padding: 20px;
        box-shadow: 0 10px 40px rgba(0,0,0,0.3);
    `;
    
    // Create image
    const img = document.createElement('img');
    img.src = dataUrl;
    img.alt = alt;
    img.style.cssText = `
        max-width: 100%;
        max-height: 70vh;
        width: auto;
        height: auto;
        border-radius: 4px;
        display: block;
        object-fit: contain;
    `;
    
    // Create close button
    const closeBtn = document.createElement('button');
    closeBtn.innerHTML = '<i class="fas fa-times"></i>';
    closeBtn.style.cssText = `
        position: absolute;
        top: 10px;
        right: 10px;
        background: rgba(0,0,0,0.7);
        color: white;
        border: none;
        border-radius: 50%;
        width: 40px;
        height: 40px;
        cursor: pointer;
        font-size: 16px;
        z-index: 10001;
    `;
    
    // Create title
    const title = document.createElement('div');
    title.textContent = alt || 'Imagen';
    title.style.cssText = `
        margin-bottom: 10px;
        font-weight: 600;
        color: var(--text-primary);
        text-align: center;
    `;
    
    // Create dimensions display
    const dimensions = document.createElement('div');
    dimensions.style.cssText = `
        margin-top: 10px;
        padding: 8px 12px;
        background: var(--bg-secondary);
        border-radius: 4px;
        font-size: 12px;
        color: var(--text-secondary);
        text-align: center;
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 20px;
    `;
    
    // Update dimensions when image loads
    img.onload = () => {
        const width = img.naturalWidth;
        const height = img.naturalHeight;
        dimensions.innerHTML = `
            <span><strong>Dimensiones:</strong> ${width} × ${height} px</span>
            <span><strong>Tamaño:</strong> ${formatFileSize(dataUrl.length * 0.75)}</span>
        `;
    };
    
    // Assemble modal
    imgContainer.appendChild(title);
    imgContainer.appendChild(img);
    imgContainer.appendChild(dimensions);
    imgContainer.appendChild(closeBtn);
    modal.appendChild(imgContainer);
    
    // Event handlers
    const closeModal = () => {
        document.body.removeChild(modal);
        document.removeEventListener('keydown', handleEscape);
    };
    
    const handleEscape = (e) => {
        if (e.key === 'Escape') closeModal();
    };
    
    closeBtn.onclick = closeModal;
    modal.onclick = (e) => {
        if (e.target === modal) closeModal();
    };
    imgContainer.onclick = (e) => e.stopPropagation();
    
    document.addEventListener('keydown', handleEscape);
    document.body.appendChild(modal);
    
    // Focus for accessibility
    closeBtn.focus();
}

function removeImage(imageId) {
    if (confirm('¿Estás seguro de que quieres eliminar esta imagen?')) {
        const wrapper = document.querySelector(`[data-image-id="${imageId}"]`);
        if (wrapper) {
            wrapper.remove();
            
            // Trigger auto-save
            if (window.autoSaveTimeout) {
                clearTimeout(window.autoSaveTimeout);
            }
            // Save only when save button is clicked
            showToast('Imagen eliminada', 'success');
        }
    }
}

function optimizeDescriptionForStorage(description) {
    const tempDiv = document.createElement('div');
    tempDiv.innerHTML = description;
    
    // Optimize images by reducing quality
    const images = tempDiv.querySelectorAll('img');
    images.forEach(img => {
        const src = img.src || img.getAttribute('src');
        if (src && src.startsWith('data:image/')) {
            try {
                // Reduce image quality for storage
                const base64Data = src.split(',')[1];
                if (base64Data && base64Data.length > 100000) { // Only optimize large images
                    // For now, just log - in a real implementation, you'd resize the image
                    console.log('Large image detected, consider using smaller images:', base64Data.length, 'characters');
                }
            } catch (e) {
                console.error('Error optimizing image:', e);
            }
        }
    });
    
    return tempDiv.innerHTML;
}

function cleanDescriptionContent(content) {
    // Create a temporary div to parse and clean the content
    const tempDiv = document.createElement('div');
    tempDiv.innerHTML = content;
    
    // Process all images to ensure they have proper attributes
    const images = tempDiv.querySelectorAll('img');
    images.forEach(img => {
        // Check if src is valid (data URLs are valid)
        const src = img.src || img.getAttribute('src');
        if (!src) {
            // Try to get src from data attributes
            const dataSrc = img.getAttribute('data-src');
            if (dataSrc) {
                img.setAttribute('src', dataSrc);
            } else {
                img.remove();
                return;
            }
        } else {
            // Ensure src is properly set as attribute
            img.setAttribute('src', src);
        }
        
        // Ensure alt attribute exists
        if (!img.alt) {
            img.alt = 'Imagen';
        }
        
        // Clean up style attribute
        if (img.style.cssText) {
            // Keep only safe styles
            const safeStyles = ['width', 'height', 'max-width', 'border-radius', 'border', 'box-shadow', 'margin', 'display'];
            const styleParts = img.style.cssText.split(';');
            const cleanStyles = styleParts.filter(style => {
                const property = style.split(':')[0]?.trim();
                return safeStyles.includes(property);
            }).join(';');
            img.style.cssText = cleanStyles;
        }
    });
    
    // Process file attachments
    const attachments = tempDiv.querySelectorAll('.file-attachment');
    attachments.forEach(attachment => {
        // Ensure attachment has proper structure
        if (!attachment.querySelector('i') || !attachment.querySelector('div')) {
            // Rebuild attachment structure if broken
            const fileName = attachment.textContent || 'Archivo';
            const icon = getFileIconFromName(fileName);
            attachment.innerHTML = `
                <i class="fas ${icon}" style="color: var(--primary-purple);"></i>
                <div>
                    <div style="font-weight: 500; font-size: 14px;">${escapeHtml(fileName)}</div>
                </div>
            `;
        }
    });
    
    // Clean up any script tags or dangerous content
    const scripts = tempDiv.querySelectorAll('script');
    scripts.forEach(script => script.remove());
    
    // Return cleaned HTML
    return tempDiv.innerHTML;
}

function getFileIconFromName(fileName) {
    const extension = fileName.split('.').pop()?.toLowerCase();
    switch (extension) {
        case 'jpg':
        case 'jpeg':
        case 'png':
        case 'gif':
        case 'webp':
            return 'fa-image';
        case 'xlsx':
        case 'xls':
            return 'fa-file-excel';
        case 'docx':
        case 'doc':
            return 'fa-file-word';
        case 'pdf':
            return 'fa-file-pdf';
        case 'txt':
            return 'fa-file-alt';
        case 'csv':
            return 'fa-file-csv';
        default:
            return 'fa-file';
    }
}
