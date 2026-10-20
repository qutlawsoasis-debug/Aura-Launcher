# Aura Launcher v3 — Unified Build Script
# 1. Сборка index.html (компиляция screens/* в SPA оболочку)
# 2. Сборка aura-v3-standalone.html (монолитный бандл)

$ErrorActionPreference = "Stop"

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Error "Node.js не найден в системе. Установите Node.js для выполнения сборки."
    exit 1
}

$baseDir = $PSScriptRoot

Write-Host "==> [1/2] Сборка index.html (assemble.js)..." -ForegroundColor Cyan
node (Join-Path $baseDir "assemble.js")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Ошибка при сборке index.html"
    exit $LASTEXITCODE
}

Write-Host "==> [2/2] Сборка aura-v3-standalone.html (build_standalone.js)..." -ForegroundColor Cyan
node (Join-Path $baseDir "build_standalone.js")
if ($LASTEXITCODE -ne 0) {
    Write-Error "Ошибка при сборке standalone"
    exit $LASTEXITCODE
}

Write-Host "==> Все сборки успешно завершены!" -ForegroundColor Green
