СРОЧНЫЙ ХОТФИКС (Claude). У игроков на 1.21.1 игра падает при запуске: Distant Horizons 2.2.1 (versions\1.21.1\mods\DistantHorizons-2.2.1-a-1.21.1.jar) вызывает net.caffeinemc.mods.sodium.client.gui.SodiumGameOptions, которого нет в Sodium 0.8.13 → ClassNotFoundException в SodiumAccessor.setFogOcclusion. DH включают пресеты Средний/Высокий/Ультра (релиз лаунчера v1.2.148). Отчёт подруги: C:\Users\magne\Downloads\Aura-report-20261022-2146.

СДЕЛАТЬ (Aura-Pack, только 1.21.1):
1. На Modrinth (проект distanthorizons) найти версию DH для 1.21.1, совместимую с Sodium 0.8.x (смотри changelog/зависимости). Если есть — заменить jar (сохранить суффикс .disabled как сейчас у файла в сборке), если нет — УДАЛИТЬ DH из versions\1.21.1\mods (и .disabled тоже).
2. Проверить в Services\Implementations\GraphicsPresetService.cs (только читать): если в mods остался включённый DistantHorizons*.jar у игрока, а в манифесте его нет — удалит ли его синхронизация сборки? Если НЕ удалит — в отчёт (это потребует хотфикса лаунчера, Claude решит).
3. tools\make-manifest.ps1 -TargetVersion 1.21.1, commit «fix(1.21.1): Distant Horizons crash with Sodium 0.8», push.
4. Проверка: клиент 1.21.1 через лаунчер с пресетом «Высокий» стартует до мира (или DH нет в modlist). Отчёт docs\redesign\reports\agy-hotfix-dh121-report.md, последней строкой ГОТОВО.
Делать СРАЗУ, впереди всего. Пушить можно без Claude (это хотфикс краша).
