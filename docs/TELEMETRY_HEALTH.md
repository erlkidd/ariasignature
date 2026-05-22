# Оценка здоровья диска (AriaSignature)

Версия документа: 1.1.2

## Назначение

AriaSignature вычисляет **собственную композитную оценку** по SMART/NVMe/Storage/WMI. Это не клон Hard Disk Sentinel (HDS): без vendor-specific drivedb целевая точность **~85%** при полном smartctl.

## Выходные поля API (`GET /disks`, карточка диска)

| Поле | Описание |
|------|----------|
| `healthPercent` | 0–100 или `null`, если сигналов недостаточно |
| `status` | `Ok` / `Warning` / `Critical` (согласован с процентом) |
| `healthSummary` | Краткое пояснение RU (факторы, источники) |
| `ssdLifeRemainingPercent` | Остаток ресурса SSD/NVMe |
| `telemetryConfidence` | 5–95: вес источников (smartctl, Storage, WMI) |

## Алгоритм (`DiskHealthEngine`)

1. База 100.
2. Штрафы: Reallocated, Pending, Uncorrectable (как в `DiskTelemetryRules.EstimateHealthFromSectorCounters`).
3. Ограничение по остатку SSD / NVMe `percentage_used`.
4. `PredictFailure` (WMI) или SMART `passed: false` → cap ~30, `Critical`.
5. NVMe `critical_warning` — дополнительный cap.
6. Температура: −5 при &gt; 55 °C, −15 при &gt; 70 °C.
7. Пороги статуса: ≥ 80 `Ok`, 50–79 `Warning`, &lt; 50 или EOL → `Critical`.

Публикация `healthPercent` при наличии хотя бы одного вклада: wear, секторы, predictFailure, vendor SMART 177, smartctl+telemetry.

## Источники (приоритет)

1. **smartctl** (ATA/NVMe JSON) — основной.
2. **Storage Reliability** (`MSFT_StorageReliabilityCounter`, Wear).
3. **WMI** — fallback, `PredictFailure`.

## Ограничения

- USB/RAID/виртуальные диски могут не сопоставиться с smartctl → `healthPercent` null, в `healthSummary` указана причина.
- Расхождение с HDS **±10–15%** на типовом SSD при корректном mapping — норма.
- Оценка **не заменяет** замену диска по регламенту вендора при `Critical`.

## Acceptance (ручная проверка)

На 2–3 физических дисках сравнить с HDS: при полном smartctl расхождение `healthPercent` ≤ 15 п.п.
