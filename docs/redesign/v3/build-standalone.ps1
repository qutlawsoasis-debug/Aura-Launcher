# Aura Launcher v3 — Build Standalone Runner
param()

$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "build_standalone.js"

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Error "Node.js не найден в системе. Установите Node.js для выполнения сборки."
    exit 1
}

Write-Host "Запуск сборщика aura-v3-standalone.html..." -ForegroundColor Cyan
node $scriptPath

if ($LASTEXITCODE -eq 0) {
    Write-Host "Сборка успешно завершена!" -ForegroundColor Green
} else {
    Write-Error "Ошибка при сборке standalone."
    exit $LASTEXITCODE
}
