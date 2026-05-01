# AriaSignature — development notes

## Release 0.2.5

### Стабилизация SQLite и API
- Для runtime-подключений SQLite включены `busy_timeout`, `WAL` и `synchronous=NORMAL`, чтобы снизить вероятность `database is locked` при параллельных операциях.
- В write-операциях репозиториев телеметрии, задач архивации и настроек добавлены ретраи на lock-коды SQLite (`5/6`) с коротким backoff.
- Инициализация схемы БД возвращена в последовательный startup hosted-service, чтобы убрать стартовую гонку между миграциями и фоновыми write-потоками.

### Стабилизация UI настроек SMART
- Убран двунаправленный цикл синхронизации cron и формы SMART в UI.
- Cron теперь вычисляется как производное значение формы и сохраняется атомарно по кнопке «Сохранить», без самопроизвольного переключения режима.

### Installer hotfix
- Переход на офлайн-установщик WebView2 runtime вместо bootstrapper-загрузчика.
- Сужен `CloseApplicationsFilter` до процессов AriaSignature для предсказуемого завершения установки.
- Сокращен post-install сервисный путь (без лишнего повторного stop/delete цикла), чтобы убрать зависания на финализации установки.

## Release 0.2.4

### Восстановление запуска (root-cause fix-forward)
- Закреплен единый runtime-контур запуска через `AriaSignatureService` без альтернативного локального fallback-процесса.
- Усилен self-heal регистрации/старта службы и диагностика ошибок `sc.exe` при запуске из UI.
- Для API readiness в UI добавлен двойной критерий готовности: `GET /api/v1/status` и `GET /` должны отвечать успешно перед выходом из fallback-экрана.

### Стабильность старта при блокировках SQLite
- Инициализация SQLite переведена в неблокирующий путь startup с таймаутом и безопасной деградацией без остановки host.
- В `HostOptions` закреплено игнорирование secondary background-service исключений, чтобы service host не падал из-за непервичных ошибок запуска.
- Подтвержден runtime-smoke: API поднимается и отдает `200` по `/api/v1/status` и `/`.

### Установка/переустановка
- Installer/uninstaller доведен до deterministic stop/delete службы с cleanup процессов перед копированием бинарников.
- Зафиксировано удаление каталога установки целиком на uninstall для исключения «устаревших» EXE/DLL после reinstall.

## Release 0.2.3

### Надежность телеметрии
- Реализована стратегия `smartctl-first` с повторными попытками по типам устройств (`sat`, `nvme`, `scsi`, USB-мосты) и autodetect fallback.
- Усилено сопоставление дисков по `PhysicalDriveN`, `model/serial` и признакам контроллера.
- В модель/API добавлены поля наблюдаемости: `smartCtlUsed`, `wmiUsed`, `storageReliabilityUsed`, `telemetryConfidence`, `telemetryDegradationReason`.
- Поле `TemperatureCelsius` переведено в nullable по домену/хранилищу/API/UI.

### Поставка и installer
- В release-gate добавлены проверки целостности `installer/smartctl/smartctl.exe` и `installer/smartctl/drivedb.h` (наличие + размер > 0).
- Runtime `smartctl` включается в installer и доставляется в `{app}\service\smartctl`.

### Стабильность запуска
- Устранен сценарий пустого черного/белого экрана при старте UI.
- Добавлен диагностический fallback-экран и автоматическое восстановление при позднем старте локального API.
- Исправлен сбой миграции SQLite при обновлении с дубликатами (`UNIQUE constraint failed: Disks_New.Id`) и конкурентной блокировке таблиц.

## Release 0.2.2

### Функциональные изменения
- Добавлен low-level канал телеметрии через `smartctl` (ATA/NVMe JSON) с объединением данных WMI и Storage Reliability.
- Улучшено покрытие SMART/health/temperature на оборудовании, где WMI возвращает неполные данные.
- Добавлен mapping по `PhysicalDriveN` и диагностические предупреждения при отсутствии SMART-сигналов.
- В Settings добавлены пресеты расписания SMART (interval/daily/custom Quartz).
- Улучшена валидация формы создания задач архивирования (field-level ошибки и понятные сообщения).
- Устранен побочный эффект фильтра журнала: смена фильтра больше не запускает refresh дисков.

### Runtime и shell
- Исправлен crash при восстановлении окна из трея (некорректная комбинация `ShowActivated=false` + `WindowState=Maximized`).
- Сохранено single-instance поведение с активацией существующего процесса.

### UI и консистентность
- Нормализовано отображение статусов службы на русском языке.
- Унифицирован стиль панелей/контролов и иерархия разделов.
- Кнопки title bar выровнены вправо; drag из maximized приближен к системному поведению окна.

### Документация
- Документация user/API/developer/installer/release-gate приведена к единому техническому стилю.
- Уточнена целевая модель: автономная работа службы + публикация данных через API.

## Release 0.2.1

- Stabilized API startup and Swagger compatibility.
- Hardened WMI telemetry collection and refresh endpoint behavior.
- Improved installer service registration flow (`sc create`, quoting, verification).
- Synchronized versions across build artifacts and docs.

## Branching policy

- Release branch: `production`.
- Working branch: `test/agent-work`.
- Merge to `production` is controlled by repository owner.
