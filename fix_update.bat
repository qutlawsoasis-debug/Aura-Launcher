@echo off
chcp 65001 >nul 2>&1
title Aura Launcher - Исправление обновления
echo.
echo  ══════════════════════════════════════════
echo   Aura Launcher - Исправление обновления
echo  ══════════════════════════════════════════
echo.
echo  [1/3] Закрываю лаунчер...
taskkill /f /im AuraLauncher.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo  [2/3] Скачиваю обновление...
set "URL=https://github.com/qutlawsoasis-debug/Aura-Launcher/releases/latest/download/AuraLauncher-win-Setup.exe"
set "OUT=%TEMP%\AuraLauncher-Setup.exe"
curl -L -o "%OUT%" "%URL%" 2>nul
if not exist "%OUT%" (
    echo  [!] curl не сработал, пробую PowerShell...
    powershell -Command "Invoke-WebRequest -Uri '%URL%' -OutFile '%OUT%'"
)
if not exist "%OUT%" (
    echo.
    echo  [X] Не удалось скачать. Проверь интернет.
    pause
    exit /b 1
)

echo  [3/3] Устанавливаю...
start "" "%OUT%"
echo.
echo  Готово! Установщик запущен.
echo  После установки запусти лаунчер как обычно.
echo.
pause
