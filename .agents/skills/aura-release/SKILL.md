---
name: aura-release
description: >-
  Пошаговый регламент выпуска релиза Aura Launcher (Velopack + GitHub Releases) и публикации
  обновлений сборки модов Aura-Pack (manifest.json). Используй при сборке релизов и деплое.
---

# Выпуск релизов Aura Launcher и Aura-Pack

Используй этот навык, когда пользователь просит собрать или опубликовать новую версию лаунчера (`Aura-Launcher`) или обновить сборку модов (`Aura-Pack`).

---

## 1. Релиз лаунчера (`Aura-Launcher`)

Путь репозитория: `C:\Users\magne\Documents\GitHub\Aura-Launcher`

### Шаг 1: Предварительная проверка
1. Запусти проверку дизайн-системы:
   ```powershell
   .\scripts\check-design.ps1
   ```
2. Запусти тесты и убедись, что проект собирается без ошибок:
   ```powershell
   dotnet test tests/AuraLauncher.Tests/AuraLauncher.Tests.csproj --filter "Category!=Integration"
   ```

### Шаг 2: Обновление версии
Обнови версию синхронно в двух файлах:
1. `AuraLauncher.csproj` -> `<Version>1.2.XX</Version>`
2. `version.json`:
   - `"internalVersion": "1.2.XX"`
   - `"userFacingVersion": "beta 1.0.YY"` (где `YY = XX - 8`, например для `1.2.45` это `beta 1.0.37`).

### Шаг 3: Коммит и пуш в `main`
```powershell
git add AuraLauncher.csproj version.json
git commit -m "release: v1.2.XX (beta 1.0.YY)"
git push origin main
```

### Шаг 4: Сборка установщика и дельта-пакетов Velopack
```powershell
.\scripts\build-release.ps1
```
Готовые артефакты появятся в `C:\AuraRelease\1.2.XX\`.
- **Правило хранения в `C:\AuraRelease`**: хранить **не более 5 последних версий** (текущая + 4 предыдущие для генерации дельта-пакетов). Все более старые папки и `.nupkg` в `C:\AuraRelease` должны автоматически удаляться скриптом `build-release.ps1` (или вручную при проверке).

### Шаг 5: Публикация в GitHub Releases
```powershell
gh release create v1.2.XX `
  "C:\AuraRelease\1.2.XX\AuraLauncher-win-Setup.exe" `
  "C:\AuraRelease\1.2.XX\AuraLauncher-win-Portable.zip" `
  "C:\AuraRelease\1.2.XX\AuraLauncher-1.2.XX-full.nupkg" `
  "C:\AuraRelease\1.2.XX\AuraLauncher-1.2.XX-delta.nupkg" `
  "C:\AuraRelease\1.2.XX\RELEASES" `
  "C:\AuraRelease\1.2.XX\releases.win.json" `
  "C:\AuraRelease\1.2.XX\assets.win.json" `
  --title "beta 1.0.YY (v1.2.XX)" --notes "beta 1.0.YY"
```

---

## 2. Обновление сборки модов (`Aura-Pack`)

Путь репозитория: `C:\Users\magne\Documents\GitHub\Aura-Pack`

1. Убедись, что в `config/` и `options.txt` не попали личные пути (`C:\Users\...`, `D:\`), никнеймы или токены.
2. Запусти скрипт автоматической публикации:
   ```powershell
   .\tools\publish.ps1 -Yes
   ```
   Скрипт сам пересчитает `manifest.json` (SHA-256, размеры файлов, `packVersion` в формате UTC `yyyyMMdd.HHmm`), создаст коммит и отправит его в ветку `main`.
3. Лаунчер у игроков автоматически подтянет обновлённые файлы при нажатии «Играть» или «Создать лобби».
