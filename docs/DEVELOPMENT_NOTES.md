# AriaSignature — development notes

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
