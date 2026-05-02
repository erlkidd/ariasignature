# smartctl (состав инсталлятора)

Файлы `smartctl.exe` и `drivedb.h` **не хранятся в Git** (см. корневой `.gitignore`). Их подготавливает [`scripts/release-gate.ps1`](../../scripts/release-gate.ps1) на машине сборки: из `PATH`, переменной окружения или загрузкой smartmontools.

Для локальной ручной сборки установщика без gate положите бинарник и `drivedb.h` в эту папку вручную. Подробности — [`docs/RELEASE_GATE.md`](../../docs/RELEASE_GATE.md) и [`docs/INSTALLER.md`](../../docs/INSTALLER.md).
