# Script para ejecutar el servidor ScrumSystem con permisos elevados
$projectPath = "D:\Proyecto_Tesis\ScrumSystem.Api\ScrumSystem.Api.csproj"
$urls = "http://localhost:5154"

Write-Host "Iniciando servidor ScrumSystem..." -ForegroundColor Cyan
Write-Host "URL: $urls" -ForegroundColor Yellow
Write-Host ""

try {
    Set-Location "D:\Proyecto_Tesis"
    dotnet run --project $projectPath --urls $urls --no-launch-profile
} catch {
    Write-Host "Error al iniciar: $_" -ForegroundColor Red
    Read-Host "Presiona Enter para cerrar"
}
