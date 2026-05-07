$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5154'

try {
    $users = Invoke-RestMethod -Uri "$base/api/users" -Method Get
    Write-Host "Users count: $($users.Count)"
    $userId = $users[0].id
    Write-Host "Using user: $userId - $($users[0].name)"
} catch {
    Write-Host "Failed fetching users: $_"
    exit 2
}

try {
    $stories = Invoke-RestMethod -Uri "$base/api/stories" -Method Get
    Write-Host "Stories count: $($stories.Count)"
} catch {
    Write-Host "Failed fetching stories: $_"
    exit 3
}

$foundTaskId = $null
foreach ($s in $stories) {
    try {
        $detail = Invoke-RestMethod -Uri "$base/api/stories/$($s.id)" -Method Get
        if ($detail.tasks -and $detail.tasks.Count -gt 0) {
            $foundTaskId = $detail.tasks[0].id
            Write-Host "Found existing task $foundTaskId in story $($s.id)"
            break
        }
    } catch {
        Write-Host "Error loading story $($s.id): $_"
    }
}

if (-not $foundTaskId) {
    Write-Host "No existing tasks found. Creating a task in first story..."
    $storyId = $stories[0].id
    $body = @{ storyId = $storyId; title = 'auto assign test task'; priority = 1 } | ConvertTo-Json
    try {
        $created = Invoke-RestMethod -Uri "$base/api/tasks" -Method Post -Body $body -ContentType "application/json"
        $foundTaskId = $created.id
        Write-Host "Created task $foundTaskId"
    } catch {
        Write-Host "Failed creating task: $_"
        exit 4
    }
}

Write-Host "Assigning task $foundTaskId to user $userId"
$payload = @{ assignedToId = $userId } | ConvertTo-Json
try {
    $assignResp = Invoke-RestMethod -Uri "$base/api/tasks/$foundTaskId/assign" -Method Patch -Body $payload -ContentType "application/json"
    Write-Host "Assign response:"
    $assignResp | ConvertTo-Json -Depth 5
} catch {
    Write-Host "Assign failed: $_"
    exit 5
}

try {
    $task = Invoke-RestMethod -Uri "$base/api/tasks/$foundTaskId" -Method Get
    Write-Host "Task after assign:"
    $task | ConvertTo-Json -Depth 5
} catch {
    Write-Host "Failed fetching task after assign: $_"
    exit 6
}

exit 0
