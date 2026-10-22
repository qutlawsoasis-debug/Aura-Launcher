# Для агентов Antigravity: вместо завершения хода — ждать изменений в доске/задачах/проверках.
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File .claude\tools\agy-wait.ps1
# Возвращается, как только что-то изменилось (печатает что), или через 7 минут ("TIMEOUT").
$root = 'C:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign'
$files = @("$root\reports\PIPELINE.md", "$root\reports\BACKLOG.md", "$root\reports\RC-TONIGHT.md", "$root\reports\release-queue.md")
$files += Get-ChildItem "$root\qa" -Recurse -Filter qa.md -ErrorAction SilentlyContinue | ForEach-Object FullName
$snap = @{}; foreach ($f in $files) { if (Test-Path -LiteralPath $f) { $snap[$f] = (Get-Item -LiteralPath $f).LastWriteTimeUtc } }
$qaCount = @($files | Where-Object { $_ -like '*\qa\*' }).Count
$deadline = (Get-Date).AddMinutes(7)
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 15
  $now = @(Get-ChildItem "$root\qa" -Recurse -Filter qa.md -ErrorAction SilentlyContinue | ForEach-Object FullName)
  if ($now.Count -ne $qaCount) { "CHANGED: новый отчёт QA"; exit 0 }
  foreach ($f in $snap.Keys) {
    if ((Test-Path -LiteralPath $f) -and (Get-Item -LiteralPath $f).LastWriteTimeUtc -ne $snap[$f]) { "CHANGED: $f"; exit 0 }
  }
}
"TIMEOUT: изменений нет — продолжай свою задачу или возьми следующую из BACKLOG.md, затем снова запусти этот скрипт"
