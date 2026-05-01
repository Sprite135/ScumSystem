// API Configuration
const API_URL = '';

// Global State
let currentUser = null;
let projects = [];
let burndownChart = null;
let membersToAdd = [];
let nextSprintNumber = 2; // Sprint 1 already exists, next will be Sprint 2

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
    
    // Load page from hash on initial load
    const hash = window.location.hash.substring(1);
    if (hash) {
        const pageName = hash.split('/')[0];
        setTimeout(() => loadPage(pageName), 100);
    }
});

// ==================== AUTHENTICATION ====================
function checkAuth() {
    const user = localStorage.getItem('scrumUser');
    if (user) {
        currentUser = JSON.parse(user);
        showMainApp();
        loadPage('dashboard');
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
            const roleMap = { 0: 'Admin', 1: 'Product Owner', 2: 'Scrum Master', 3: 'Developer' };
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
        renderProjects();
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
    
    container.innerHTML = projects.map(p => {
        const iconHtml = iconMap[p.icon] || iconMap['folder'];
        const iconStyle = p.color ? `background: ${p.color};` : '';
        const isActive = selectedProjectId === p.id ? 'active' : '';
        
        // Check if current user is creator
        const currentUserId = currentUser?.id?.toString().toLowerCase();
        const projectOwnerId = p.productOwnerId?.toString().toLowerCase();
        const isCreator = currentUserId && projectOwnerId && currentUserId === projectOwnerId;
        
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
                    ${isCreator 
                        ? `<div class="project-menu-item" onclick="event.stopPropagation(); openConfigureProjectModal('${p.id}', '${escapeHtml(p.name)}', '${escapeHtml(p.key || '')}', '${escapeHtml(p.color || '')}', '${escapeHtml(p.icon || '')}')">
                            <i class="fas fa-cog"></i> Configurar
                           </div>
                           <div class="project-menu-item text-danger" onclick="event.stopPropagation(); deleteProject('${p.id}', '${escapeHtml(p.name)}')">
                            <i class="fas fa-trash"></i> Eliminar proyecto
                           </div>`
                        : `<div class="project-menu-item text-warning" onclick="event.stopPropagation(); leaveProject('${p.id}', '${escapeHtml(p.name)}')">
                            <i class="fas fa-sign-out-alt"></i> Salir del proyecto
                           </div>`
                    }
                </div>
            </div>
        `;
    }).join('');
}

function selectProject(projectId) {
    selectedProjectId = projectId;
    renderSidebarProjects();
    // Navigate to project view with backlog as default sub-tab
    window.location.hash = `project/${projectId}/backlog`;
    loadPage('project');
}

let projectSubTab = 'backlog';

function loadProjectView() {
    const mainContent = document.getElementById('main-content');
    if (!mainContent) return;
    
    const hash = window.location.hash;
    const parts = hash.split('/');
    const projectId = parts[1];
    projectSubTab = parts[2] || 'backlog';
    
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
    
    if (projectSubTab === 'backlog') {
        // Load backlog view
        loadBacklog();
    } else if (projectSubTab === 'board') {
        // Load kanban board directly in project view
        loadProjectBoard();
    }
}

async function loadProjectBoard() {
    const content = document.getElementById('project-content');
    if (!content || !selectedProjectId) return;
    
    try {
        const data = await apiRequest(`/api/stories/project/${selectedProjectId}/board`);
        boardMembers = data.members || [];
        
        // Render kanban board HTML
        content.innerHTML = `
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

function loadProjectMembers() {
    console.log('loadProjectMembers called, selectedProjectId:', selectedProjectId);
    console.log('Projects array:', projects);
    
    if (!selectedProjectId) {
        console.log('No selectedProjectId');
        return;
    }
    
    // Get project from projects array
    const project = projects.find(p => p.id === selectedProjectId);
    console.log('Found project:', project);
    
    if (!project) {
        console.log('Project not found');
        return;
    }
    
    if (!project.members) {
        console.log('Project has no members');
        return;
    }
    
    console.log('Project members:', project.members);
    
    const membersContainer = document.getElementById('project-members-list');
    if (!membersContainer) {
        console.log('Members container not found');
        return;
    }
    
    // Show member avatars (max 2)
    const membersToShow = project.members.slice(0, 2);
    const remainingCount = project.members.length - 2;
    
    console.log('Members to show:', membersToShow);
    console.log('Remaining count:', remainingCount);
    
    let avatarsHtml = membersToShow.map(member => {
        const initials = member.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2);
        return `<div class="member-avatar" title="${escapeHtml(member.name)} (${member.role})">${initials}</div>`;
    }).join('');
    
    if (remainingCount > 0) {
        avatarsHtml += `<div class="member-avatar more-members" title="+${remainingCount} miembros más">+${remainingCount}</div>`;
    }
    
    console.log('Setting avatars HTML:', avatarsHtml);
    membersContainer.innerHTML = avatarsHtml;
}

function searchProjectStories(event) {
    const searchTerm = event.target.value.toLowerCase();
    
    if (projectSubTab === 'backlog') {
        // Filter backlog stories
        const storyItems = document.querySelectorAll('#backlog-list .story-item');
        storyItems.forEach(item => {
            const title = item.querySelector('.story-title')?.textContent.toLowerCase() || '';
            const description = item.querySelector('.story-description')?.textContent.toLowerCase() || '';
            const matches = title.includes(searchTerm) || description.includes(searchTerm);
            item.style.display = matches ? '' : 'none';
        });
    } else if (projectSubTab === 'board') {
        // Filter kanban cards
        const cards = document.querySelectorAll('.kanban-card');
        cards.forEach(card => {
            const title = card.querySelector('.kanban-card-title')?.textContent.toLowerCase() || '';
            const key = card.querySelector('.kanban-card-key')?.textContent.toLowerCase() || '';
            const matches = title.includes(searchTerm) || key.includes(searchTerm);
            card.style.display = matches ? '' : 'none';
        });
    }
}

function toggleFilterMenu() {
    const menu = document.getElementById('filter-menu');
    if (menu) {
        menu.style.display = menu.style.display === 'none' ? 'block' : 'none';
    }
}

function applyFilter(filterType) {
    // Close filter menu
    document.getElementById('filter-menu').style.display = 'none';
    
    // Apply filter logic based on type
    console.log('Applying filter:', filterType);
    
    // For now, just show a toast. Later we can implement actual filtering
    const filterNames = {
        'main': 'Principal',
        'sprint': 'Sprint',
        'assignee': 'Persona asignada',
        'activity': 'Tipo de actividad',
        'labels': 'Etiquetas',
        'status': 'Estado',
        'priority': 'Prioridad'
    };
    
    showToast(`Filtro aplicado: ${filterNames[filterType]}`);
    
    // Close filter menu
    document.getElementById('filter-menu').style.display = 'none';
}

// Close filter menu when clicking outside
document.addEventListener('click', (e) => {
    if (!e.target.closest('.project-filter')) {
        const filterMenu = document.getElementById('filter-menu');
        if (filterMenu) filterMenu.style.display = 'none';
    }
});

// ==================== COLUMN DRAG AND DROP ====================
let draggedColumn = null;

function startDragColumn(event, headerElement) {
    // Don't start drag if clicking on delete button
    if (event.target.closest('.column-delete-btn')) {
        return;
    }
    
    const column = headerElement.closest('.kanban-column');
    if (!column) return;
    
    draggedColumn = column;
    column.style.opacity = '0.5';
    
    document.addEventListener('mousemove', handleColumnDrag);
    document.addEventListener('mouseup', handleColumnDrop);
    
    event.preventDefault();
}

function handleColumnDrag(event) {
    if (!draggedColumn) return;
    
    const board = document.getElementById('kanban-board');
    const columns = board.querySelectorAll('.kanban-column');
    
    columns.forEach(column => {
        if (column === draggedColumn) return;
        
        const rect = column.getBoundingClientRect();
        const midpoint = rect.left + rect.width / 2;
        
        if (event.clientX < midpoint) {
            column.style.borderLeft = '3px solid var(--primary-purple)';
            column.style.borderRight = '';
        } else {
            column.style.borderRight = '3px solid var(--primary-purple)';
            column.style.borderLeft = '';
        }
    });
}

function handleColumnDrop(event) {
    if (!draggedColumn) return;
    
    const board = document.getElementById('kanban-board');
    const columns = board.querySelectorAll('.kanban-column');
    
    columns.forEach(column => {
        column.style.borderLeft = '';
        column.style.borderRight = '';
    });
    
    // Find the column to drop before/after
    let dropTarget = null;
    let dropPosition = 'before';
    
    columns.forEach(column => {
        if (column === draggedColumn) return;
        
        const rect = column.getBoundingClientRect();
        const midpoint = rect.left + rect.width / 2;
        
        if (event.clientX >= rect.left && event.clientX <= rect.right) {
            dropTarget = column;
            dropPosition = event.clientX < midpoint ? 'before' : 'after';
        }
    });
    
    if (dropTarget) {
        if (dropPosition === 'before') {
            board.insertBefore(draggedColumn, dropTarget);
        } else {
            board.insertBefore(draggedColumn, dropTarget.nextSibling);
        }
    }
    
    draggedColumn.style.opacity = '';
    draggedColumn = null;
    
    document.removeEventListener('mousemove', handleColumnDrag);
    document.removeEventListener('mouseup', handleColumnDrop);
}

function showAddColumnModal() {
    const columnName = prompt('Nombre de la nueva columna:');
    if (!columnName || columnName.trim() === '') return;
    
    addNewColumn(columnName.trim());
}

function addNewColumn(name) {
    const board = document.getElementById('kanban-board');
    const addBtn = board.querySelector('.add-column-btn');
    
    const columnId = 'column-' + Date.now();
    const statusId = 'Status-' + Date.now();
    
    const newColumn = document.createElement('div');
    newColumn.className = 'kanban-column';
    newColumn.setAttribute('data-status', statusId);
    newColumn.setAttribute('data-custom', 'true');
    newColumn.draggable = true;
    
    newColumn.innerHTML = `
        <div class="kanban-column-header" onmousedown="startDragColumn(event, this)">
            <i class="fas fa-grip-vertical column-drag-handle"></i>
            <span class="column-title">${escapeHtml(name)}</span>
            <span class="column-count" id="count-${columnId}">0</span>
            <button class="column-delete-btn" onclick="event.stopPropagation(); deleteColumn(this, '${escapeHtml(name)}')" title="Eliminar columna">
                <i class="fas fa-times"></i>
            </button>
        </div>
        <div class="kanban-column-content" id="${columnId}" ondrop="drop(event, '${statusId}')" ondragover="allowDrop(event)">
        </div>
    `;
    
    board.insertBefore(newColumn, addBtn);
    showToast(`Columna "${name}" agregada`);
}

function deleteColumn(button, columnName) {
    if (!confirm(`¿Eliminar la columna "${columnName}"? Las historias en esta columna se moverán a "Por hacer".`)) {
        return;
    }
    
    const column = button.closest('.kanban-column');
    if (!column) return;
    
    // Move stories to Backlog
    const stories = column.querySelectorAll('.kanban-card');
    const backlogColumn = document.querySelector('[data-status="Backlog"] .kanban-column-content');
    
    stories.forEach(story => {
        if (backlogColumn) {
            backlogColumn.appendChild(story);
        }
    });
    
    // Remove column
    column.remove();
    showToast(`Columna "${columnName}" eliminada`);
}

function toggleProjectMenu(projectId, projectName, isCreator) {
    // Close all other menus first
    document.querySelectorAll('.project-menu').forEach(menu => {
        if (menu.id !== `project-menu-${projectId}`) {
            menu.style.display = 'none';
        }
    });
    
    const menu = document.getElementById(`project-menu-${projectId}`);
    if (menu) {
        menu.style.display = menu.style.display === 'none' ? 'block' : 'none';
    }
}

function openConfigureProjectModal(projectId, name, key, color, icon) {
    document.getElementById('configure-project-id').value = projectId;
    document.getElementById('configure-project-name').value = name;
    document.getElementById('configure-project-key').value = key;
    document.getElementById('configure-project-color').value = color || '#8b5cf6';
    document.getElementById('configure-project-icon').value = icon || 'folder';
    showModal('configure-project-modal');
}

// Configure project form handler
document.getElementById('configure-project-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    
    const projectId = document.getElementById('configure-project-id').value;
    const name = document.getElementById('configure-project-name').value;
    const key = document.getElementById('configure-project-key').value;
    const color = document.getElementById('configure-project-color').value;
    const icon = document.getElementById('configure-project-icon').value;
    
    try {
        await apiRequest(`/api/projects/${projectId}?userId=${currentUser.id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, key, color, icon, userId: currentUser.id })
        });
        
        showToast('Proyecto actualizado');
        hideModal('configure-project-modal');
        loadProjects();
    } catch (error) {
        showToast('Error: ' + error.message, 'error');
    }
});

// Close menu when clicking outside
document.addEventListener('click', (e) => {
    if (!e.target.closest('.sidebar-project-item')) {
        document.querySelectorAll('.project-menu').forEach(menu => {
            menu.style.display = 'none';
        });
    }
});

function renderProjects() {
    const container = document.getElementById('projects-list');
    if (!container) return;
    
    if (projects.length === 0) {
        container.innerHTML = '<div class="empty-state"><i class="fas fa-folder-open"></i><p>No hay proyectos. Crea uno nuevo.</p></div>';
        return;
    }
    
    container.innerHTML = projects.map(p => {
        // Get icon based on stored value
        const iconMap = {
            'folder': '<i class="fas fa-folder"></i>',
            'rocket': '<i class="fas fa-rocket"></i>',
            'code': '<i class="fas fa-code"></i>',
            'mobile': '<i class="fas fa-mobile-alt"></i>',
            'globe': '<i class="fas fa-globe"></i>'
        };
        const iconHtml = iconMap[p.icon] || iconMap['folder'];
        
        // Use stored color or default gradient
        const iconStyle = p.color ? `background: ${p.color};` : '';
        
        // Use stored key or generate from ID
        const projectKey = p.key ? p.key : `PROJ-${p.id.substring(0, 4).toUpperCase()}`;
        
        // Check if current user is the creator (compare as strings)
        const currentUserId = currentUser?.id?.toString().toLowerCase();
        const projectOwnerId = p.productOwnerId?.toString().toLowerCase();
        const isCreator = currentUserId && projectOwnerId && currentUserId === projectOwnerId;
        
        return `
        <div class="project-card">
            <div class="project-card-header">
                <div class="project-icon" style="${iconStyle}">
                    ${iconHtml}
                </div>
                <div class="project-title">
                    <h4>${escapeHtml(p.name)}</h4>
                    <span class="project-key">${projectKey}</span>
                    ${p.creatorName ? `<span class="project-creator">👤 ${escapeHtml(p.creatorName)}</span>` : ''}
                </div>
                <div class="project-actions">
                    <button class="btn btn-icon btn-small" onclick="editProject('${p.id}')" title="Editar">
                        <i class="fas fa-edit"></i>
                    </button>
                    <button class="btn btn-icon btn-small" onclick="openAddMembersModal('${p.id}', '${escapeHtml(p.name)}')" title="Agregar miembros">
                        <i class="fas fa-user-plus"></i>
                    </button>
                    ${isCreator 
                        ? `<button class="btn btn-icon btn-small text-danger" onclick="deleteProject('${p.id}')" title="Eliminar proyecto">
                            <i class="fas fa-trash"></i>
                        </button>`
                        : `<button class="btn btn-icon btn-small text-warning" onclick="leaveProject('${p.id}', '${escapeHtml(p.name)}')" title="Salir del proyecto">
                            <i class="fas fa-sign-out-alt"></i>
                        </button>`
                    }
                </div>
            </div>
            <p class="project-description">${escapeHtml(p.description || 'Sin descripción')}</p>
            <div class="project-stats">
                <div class="stat-item">
                    <i class="fas fa-book"></i>
                    <span>Historias</span>
                    <strong>0</strong>
                </div>
                <div class="stat-item">
                    <i class="fas fa-star"></i>
                    <span>Points</span>
                    <strong>0</strong>
                </div>
                <div class="stat-item">
                    <i class="fas fa-users"></i>
                    <span>Miembros</span>
                    <strong>${p.members?.length || 0}</strong>
                </div>
            </div>
            <div class="project-footer">
                <div class="project-members">
                    ${p.members?.slice(0, 3).map((m, i) => {
                        const initials = m.name?.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2) || '?';
                        return `<div class="member-avatar" style="margin-left: ${i > 0 ? '-8px' : '0'}; z-index: ${10-i};">${initials}</div>`;
                    }).join('') || '<span class="text-muted" style="font-size: 12px;">Sin miembros</span>'}
                    ${p.members?.length > 3 ? `<div class="member-avatar" style="margin-left: -8px; background: var(--bg-hover); color: var(--text-muted);">+${p.members.length - 3}</div>` : ''}
                </div>
                <span class="project-date">
                    <i class="fas fa-calendar"></i> ${new Date(p.createdAt).toLocaleDateString()}
                </span>
            </div>
        </div>
    `}).join('');
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
            showToast('Usuario no encontrado', 'error');
        }
    } catch (error) {
        if (error.message.includes('404') || error.message.includes('Not Found')) {
            showToast('Usuario no encontrado', 'error');
        } else {
            showToast('Error: ' + error.message, 'error');
        }
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
        
        showToast(`${membersToAdd.length} miembro(s) agregado(s)`);
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
    const options = projects.map(p => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('');
    document.querySelectorAll('#story-project').forEach(el => {
        if (el) el.innerHTML = '<option value="">Seleccionar...</option>' + options;
    });
}

async function loadBacklog() {
    console.log('loadBacklog called, selectedProjectId:', selectedProjectId);
    
    const content = document.getElementById('project-content');
    if (!content || !selectedProjectId) {
        console.log('No content or selectedProjectId');
        return;
    }
    
    try {
        console.log('Fetching stories for project:', selectedProjectId);
        const data = await apiRequest(`/api/stories/project/${selectedProjectId}/backlog`);
        console.log('Data received:', data);
        renderProjectBacklog(data.stories || [], data.members || []);
    } catch (error) {
        console.error('Error loading project backlog:', error);
        content.innerHTML = '<p class="empty-state">Error al cargar backlog</p>';
    }
}

function renderProjectBacklog(stories, members) {
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
    
    // Group stories by status
    const backlogStories = stories.filter(s => s.status === 'Backlog');
    const inProgressStories = stories.filter(s => s.status === 'InProgress');
    const doneStories = stories.filter(s => s.status === 'Done');
    
    // Calculate story points
    const backlogPoints = backlogStories.reduce((sum, s) => sum + (s.storyPoints || 0), 0);
    const inProgressPoints = inProgressStories.reduce((sum, s) => sum + (s.storyPoints || 0), 0);
    const donePoints = doneStories.reduce((sum, s) => sum + (s.storyPoints || 0), 0);
    
    content.innerHTML = `
        <div class="backlog-container">
            <!-- Sprint Section -->
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
                        <span class="backlog-section-title">Sprint 1 <span class="activity-count">(${stories.length} actividades)</span></span>
                    </div>
                    <div class="backlog-header-right">
                        <div class="backlog-badges">
                            <div class="backlog-badge todo" title="Pendiente">${backlogPoints}</div>
                            <div class="backlog-badge inprogress" title="En curso">${inProgressPoints}</div>
                            <div class="backlog-badge done" title="Hecho">${donePoints}</div>
                        </div>
                        <button type="button" class="create-sprint-btn" onclick="showStartSprintModal(${stories.length})" ${stories.length === 0 ? 'disabled' : ''}>Iniciar sprint</button>
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
                <div class="sprint-content" id="sprint-content">
                    <!-- Stories will be rendered here -->
                </div>
            </div>
            
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
                <div class="backlog-content" id="backlog-content">
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
    
    // Render stories in sprint content
    const sprintContent = document.getElementById('sprint-content');
    if (sprintContent) {
        sprintContent.innerHTML = stories.map(story => createBacklogStoryCard(story, members, projectKey)).join('');
    }
    
    // Render backlog stories
    const backlogContent = document.getElementById('backlog-content');
    if (backlogContent) {
        backlogContent.innerHTML = backlogStories.map(story => createBacklogStoryCard(story, members, projectKey)).join('');
    }
}

function createBacklogStoryCard(story, members, projectKey) {
    const assignee = members.find(m => m.id === story.assigneeId);
    const assigneeInitials = assignee ? assignee.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2) : '?';
    
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
        <div class="backlog-story-card" data-story-id="${story.id}">
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
                    <div class="assignee-avatar" title="${assignee.name} (${assignee.role})">
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

function changeStoryStatus(storyId, newStatus) {
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
    
    const card = document.querySelector(`[data-story-id="${storyId}"]`);
    if (card) {
        const badge = card.querySelector('.story-status-badge');
        if (badge) {
            badge.innerHTML = `${statusLabels[newStatus]} <i class="fas fa-chevron-down" style="margin-left: 6px; font-size: 10px;"></i>`;
        }
    }
    
    showToast(`Estado actualizado a: ${statusLabels[newStatus]}`, 'success');
    
    // Here you would also make an API call to persist the change
    // apiRequest(`/api/stories/${storyId}/status`, { method: 'PUT', body: JSON.stringify({ status: newStatus }) });
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

function deleteBacklogStory(storyId) {
    // Close the menu
    const menu = document.getElementById(`story-actions-menu-${storyId}`);
    if (menu) menu.style.display = 'none';
    
    // Confirm deletion
    if (!confirm('¿Estás seguro de que deseas eliminar esta historia?')) {
        return;
    }
    
    // Remove the story card from the UI
    const card = document.querySelector(`[data-story-id="${storyId}"]`);
    if (card) {
        card.remove();
        showToast('Historia eliminada', 'success');
        updateActivityCount();
        
        // Update the start sprint button state
        updateStartSprintButtonState();
    }
    
    // Here you would also make an API call to delete from backend
    // apiRequest(`/api/stories/${storyId}`, { method: 'DELETE' });
}

function editStorySummary(storyId) {
    const card = document.querySelector(`[data-story-id="${storyId}"]`);
    if (!card) return;
    
    const titleSpan = card.querySelector('.story-title');
    if (!titleSpan) return;
    
    const currentTitle = titleSpan.textContent;
    const newTitle = prompt('Editar resumen de la historia:', currentTitle);
    
    if (newTitle !== null && newTitle.trim() !== '') {
        titleSpan.textContent = newTitle;
        showToast('Resumen actualizado', 'success');
        
        // Here you would also make an API call to update the story
        // apiRequest(`/api/stories/${storyId}`, { method: 'PUT', body: JSON.stringify({ title: newTitle }) });
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

function createBacklogStory() {
    const titleInput = document.getElementById('backlog-new-story-title');
    const title = titleInput.value.trim();
    
    if (!title) {
        showToast('Por favor ingresa un título para la historia', 'error');
        return;
    }
    
    // Get project key
    const project = projects.find(p => p.id === selectedProjectId);
    let projectKey;
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
    } else {
        projectKey = 'PROJ';
    }
    
    // Create new story object
    const newStory = {
        id: 'story-' + Date.now(),
        title: title,
        key: 'PROYEC-' + Math.floor(Math.random() * 1000),
        status: 'Backlog',
        priority: 2,
        storyPoints: null,
        assigneeId: null,
        description: '',
        createdAt: new Date().toISOString()
    };
    
    // Add story to the backlog content
    const backlogContent = document.getElementById('backlog-content');
    const createContainer = document.getElementById('backlog-create-container');
    
    if (backlogContent && createContainer) {
        const storyCardHTML = createBacklogStoryCard(newStory, [], projectKey);
        if (storyCardHTML) {
            const tempDiv = document.createElement('div');
            tempDiv.innerHTML = storyCardHTML;
            const storyCard = tempDiv.firstElementChild;
            
            // Move create container after the new story
            backlogContent.appendChild(storyCard);
            backlogContent.appendChild(createContainer);
        }
    }
    
    showToast('Historia creada: ' + title, 'success');
    hideCreateStoryFormBacklog();
    
    // Update activity count
    updateActivityCount();
}

function createSprintFromBacklog() {
    const backlogContainer = document.querySelector('.backlog-container');
    if (!backlogContainer) return;
    
    const sprintNumber = nextSprintNumber++;
    const sprintId = `sprint-${sprintNumber}`;
    
    // Create new sprint section HTML
    const sprintSection = document.createElement('div');
    sprintSection.className = 'sprint-section';
    sprintSection.setAttribute('data-drop-target-for-element', 'true');
    sprintSection.id = sprintId;
    
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
                <span class="backlog-section-title">Sprint ${sprintNumber} <span class="activity-count">(0 actividades)</span></span>
            </div>
            <div class="backlog-header-right">
                <div class="backlog-badges">
                    <div class="backlog-badge todo" title="Pendiente">0</div>
                    <div class="backlog-badge inprogress" title="En curso">0</div>
                    <div class="backlog-badge done" title="Hecho">0</div>
                </div>
                <button type="button" class="create-sprint-btn" onclick="showStartSprintModal(0)" disabled>Iniciar sprint</button>
                <div class="sprint-menu-container">
                    <button type="button" class="sprint-menu-btn" onclick="alert('Boton 3 puntos clickeado: ${sprintId}'); toggleSprintOptionsMenu(event, '${sprintId}');" title="Más opciones">
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
        <div class="sprint-content" id="sprint-content-${sprintId}">
            <!-- Stories will be rendered here -->
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
    
    // Insert the new sprint section before the backlog section
    const backlogSection = backlogContainer.querySelector('.backlog-section');
    if (backlogSection) {
        backlogContainer.insertBefore(sprintSection, backlogSection);
    } else {
        backlogContainer.appendChild(sprintSection);
    }
    
    showToast(`Sprint ${sprintNumber} creado exitosamente`, 'success');
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

function createStoryForSprint(sprintId) {
    // For Sprint 1, the ID is different (no suffix)
    const inputId = sprintId === 'sprint-1' ? 'new-story-title' : `new-story-title-${sprintId}`;
    const titleInput = document.getElementById(inputId);
    const title = titleInput ? titleInput.value.trim() : '';
    
    if (!title) {
        showToast('Por favor ingresa un título para la historia', 'error');
        return;
    }
    
    // Get project key
    const project = projects.find(p => p.id === selectedProjectId);
    let projectKey;
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
    } else {
        projectKey = 'PROJ';
    }
    
    // Create new story object
    const newStory = {
        id: 'story-' + Date.now(),
        title: title,
        key: 'PROYEC-' + Math.floor(Math.random() * 1000),
        status: 'Backlog',
        priority: 2,
        storyPoints: null,
        assigneeId: null,
        description: '',
        createdAt: new Date().toISOString()
    };
    
    // Add story to the sprint content
    const sprintContent = document.getElementById(`sprint-content-${sprintId}`);
    
    if (sprintContent) {
        const storyCardHTML = createBacklogStoryCard(newStory, [], projectKey);
        if (storyCardHTML) {
            const tempDiv = document.createElement('div');
            tempDiv.innerHTML = storyCardHTML;
            const storyCard = tempDiv.firstElementChild;
            
            // Append to sprint content
            sprintContent.appendChild(storyCard);
        }
    }
    
    showToast('Historia creada: ' + title, 'success');
    hideCreateStoryFormForSprint(sprintId);
    
    // Update activity count
    updateActivityCount();
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

function handleDeleteSprintClick(event, sprintId) {
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

function showStartSprintModal(activitiesCount) {
    const modal = document.getElementById('start-sprint-modal');
    const countElement = document.getElementById('sprint-activities-count');
    
    if (modal && countElement) {
        countElement.textContent = `${activitiesCount} actividades se incluirán en este sprint.`;
        modal.classList.add('active');
        
        // Set default start date to today
        const startDateInput = document.getElementById('sprint-start-date');
        if (startDateInput) {
            const today = new Date().toISOString().split('T')[0];
            startDateInput.value = today;
        }
        
        // Reset form
        document.getElementById('start-sprint-form').reset();
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

function submitStartSprint() {
    const name = document.getElementById('sprint-name').value.trim();
    const duration = document.getElementById('sprint-duration').value;
    const startDate = document.getElementById('sprint-start-date').value;
    const endDate = document.getElementById('sprint-end-date').value;
    const goal = document.getElementById('sprint-goal').value.trim();
    
    if (!name || !duration || !startDate || !endDate) {
        showToast('Por favor completa todos los campos obligatorios', 'error');
        return;
    }
    
    // Here you would make an API call to create the sprint
    // apiRequest('/api/sprints', { method: 'POST', body: JSON.stringify({ name, duration, startDate, endDate, goal }) });
    
    showToast(`Sprint "${name}" iniciado correctamente`, 'success');
    hideModal('start-sprint-modal');
    
    // Update the UI to show sprint is active
    const startSprintBtn = document.querySelector('.start-sprint-btn');
    if (startSprintBtn) {
        startSprintBtn.textContent = 'Sprint en curso';
        startSprintBtn.disabled = true;
    }
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

function createStory() {
    const titleInput = document.getElementById('new-story-title');
    const title = titleInput.value.trim();
    
    if (!title) {
        showToast('Por favor ingresa un título para la historia', 'error');
        return;
    }
    
    // Create new story object
    const newStory = {
        id: 'story-' + Date.now(),
        title: title,
        key: 'PROYEC-' + Math.floor(Math.random() * 1000),
        status: 'Backlog',
        priority: 2,
        storyPoints: null,
        assigneeId: null,
        description: '',
        createdAt: new Date().toISOString()
    };
    
    // Add story to the sprint content
    const sprintContent = document.getElementById('sprint-content');
    const createContainer = document.getElementById('create-story-container');
    
    // Get project key
    const project = projects.find(p => p.id === selectedProjectId);
    let projectKey;
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
    } else {
        projectKey = 'PROJ';
    }
    
    if (sprintContent && createContainer) {
        const storyCardHTML = createBacklogStoryCard(newStory, [], projectKey);
        if (storyCardHTML) {
            const tempDiv = document.createElement('div');
            tempDiv.innerHTML = storyCardHTML;
            const storyCard = tempDiv.firstElementChild;
            
            // Move create container after the new story
            sprintContent.appendChild(storyCard);
            sprintContent.appendChild(createContainer);
        }
    }
    
    // Update the start sprint button state
    updateStartSprintButtonState();
    
    showToast('Historia creada: ' + title, 'success');
    hideCreateStoryForm();
    
    // Update activity count
    updateActivityCount();
}

function updateActivityCount() {
    const sprintContent = document.getElementById('sprint-content');
    const activityCountElements = document.querySelectorAll('.activity-count');
    
    if (sprintContent && activityCountElements.length > 0) {
        const storyCards = sprintContent.querySelectorAll('.backlog-story-card');
        const count = storyCards.length;
        
        activityCountElements.forEach(element => {
            element.textContent = `(${count} actividades)`;
        });
    }
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

// Close create story form when clicking outside (only for dynamic sprints)
document.addEventListener('click', function(event) {
    // Check all create story forms except Sprint 1
    document.querySelectorAll('.create-story-form').forEach(form => {
        if (form.style.display === 'block' && form.id !== 'create-story-form') {
            // Get the corresponding trigger for dynamic sprint
            const formId = form.id;
            const sprintId = formId.replace('create-story-form-', '');
            const triggerId = `create-story-trigger-${sprintId}`;
            const trigger = document.getElementById(triggerId);
            
            // Check if click is outside the form and not on the trigger button
            if (!event.target.closest('.create-story-form') && !event.target.closest('.create-story-btn')) {
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

// Original Sprint 1 handler (keep existing)
document.addEventListener('click', function(event) {
    const form = document.getElementById('create-story-form');
    const trigger = document.getElementById('create-story-trigger');
    
    // Only if form is visible and this is Sprint 1
    if (form && form.style.display === 'block') {
        // Check if click is outside the form and not on the trigger button
        if (!event.target.closest('.create-story-form') && !event.target.closest('.create-story-btn')) {
            hideCreateStoryForm();
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
    const projectId = document.getElementById('backlog-project-select')?.value || projects[0]?.id;
    
    const data = {
        projectId: projectId,
        sprintId: null,
        title: document.getElementById('story-title').value,
        description: document.getElementById('story-description').value,
        acceptanceCriteria: document.getElementById('story-criteria').value,
        storyPoints: parseInt(document.getElementById('story-points').value) || 0,
        priority: parseInt(document.getElementById('story-priority').value)
    };
    
    try {
        const url = isEdit ? `/api/stories/${id}` : '/api/stories';
        const method = isEdit ? 'PUT' : 'POST';
        await apiRequest(url, { method, body: JSON.stringify(data) });
        
        hideModal('story-modal');
        e.target.reset();
        document.getElementById('story-id').value = '';
        loadBacklog();
        showToast(isEdit ? 'Historia actualizada' : 'Historia creada');
    } catch (error) {
        showToast('Error: ' + error.message, 'error');
    }
}

async function editStory(id) {
    try {
        const story = await apiRequest(`/api/stories/${id}`);
        document.getElementById('story-id').value = story.id;
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
    if (!confirm('¿Eliminar esta historia?')) return;
    
    try {
        await apiRequest(`/api/stories/${id}`, { method: 'DELETE' });
        loadBacklog();
        showToast('Historia eliminada');
    } catch (error) {
        showToast('Error al eliminar', 'error');
    }
}

// ==================== KANBAN BOARD ====================
let boardMembers = [];

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
    // Clear all columns
    ['backlog', 'in-progress', 'done'].forEach(col => {
        const container = document.getElementById(`column-${col}`);
        if (container) container.innerHTML = '';
    });
    
    // Count stories per column
    const counts = { Backlog: 0, InProgress: 0, Done: 0 };
    
    stories.forEach(story => {
        // Map old statuses to new ones
        let status = story.status;
        if (status === 'SprintBacklog') status = 'Backlog';
        
        counts[status] = (counts[status] || 0) + 1;
        const card = createKanbanCard(story, members);
        
        let columnId;
        switch (status) {
            case 'Backlog': columnId = 'column-backlog'; break;
            case 'InProgress': columnId = 'column-in-progress'; break;
            case 'Done': columnId = 'column-done'; break;
        }
        
        const container = document.getElementById(columnId);
        if (container) container.appendChild(card);
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
    
    // Generate avatar HTML for assignee
    const assignee = members.find(m => m.id === story.assigneeId);
    const assigneeInitials = assignee ? assignee.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2) : '?';
    const assigneeAvatar = assignee 
        ? `<div class="kanban-card-assignee assigned" title="${escapeHtml(assignee.name)} (${assignee.role})">${assigneeInitials}</div>`
        : `<div class="kanban-card-assignee unassigned" title="Sin asignar">?</div>`;
    
    // Generate member avatars row (like Jira)
    const memberAvatars = members.slice(0, 3).map((m, i) => {
        const initials = m.name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2);
        const isAssigned = m.id === story.assigneeId;
        return `<div class="member-avatar ${isAssigned ? 'active' : ''}" title="${escapeHtml(m.name)}" style="z-index: ${10-i}; margin-left: ${i > 0 ? '-8px' : '0'};">${initials}</div>`;
    }).join('');
    
    const extraMembers = members.length > 3 ? `<div class="member-avatar more" style="margin-left: -8px;">+${members.length - 3}</div>` : '';
    
    card.innerHTML = `
        <div class="kanban-card-header">
            <span class="kanban-card-id">#${story.id.substring(0, 8)}</span>
            <div class="kanban-card-priority priority-${story.priority}"></div>
        </div>
        <div class="kanban-card-title">${escapeHtml(story.title)}</div>
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
        e.dataTransfer.setData('storyId', story.id);
        e.dataTransfer.effectAllowed = 'move';
    });
    
    card.addEventListener('dragend', () => {
        card.classList.remove('dragging');
        document.querySelectorAll('.kanban-column-content').forEach(col => {
            col.classList.remove('drag-over');
        });
    });
    
    return card;
}

function allowDrop(ev) {
    ev.preventDefault();
    ev.currentTarget.classList.add('drag-over');
}

async function drop(ev, newStatus) {
    ev.preventDefault();
    ev.currentTarget.classList.remove('drag-over');
    
    const storyId = ev.dataTransfer.getData('storyId');
    if (!storyId) return;
    
    try {
        await apiRequest(`/api/stories/${storyId}/status`, {
            method: 'PUT',
            body: JSON.stringify({ status: newStatus })
        });
        loadBoard();
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
    if (!confirm('¿Eliminar esta historia?')) return;
    
    try {
        await apiRequest(`/api/stories/${id}`, { method: 'DELETE' });
        loadBoard();
        showToast('Historia eliminada');
    } catch (error) {
        showToast('Error al eliminar', 'error');
    }
}

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
