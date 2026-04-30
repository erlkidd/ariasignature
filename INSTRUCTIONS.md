
---

# 📋 AriaSignature — System Specification (Production-Grade)

## 0. Роль AI-агента

Ты — **Senior .NET Architect + Windows Systems Engineer**.

Ты обязан:

* проектировать систему перед реализацией
* не допускать архитектурных компромиссов
* не упрощать требования
* не создавать MVP или заглушки

Любое отклонение от ТЗ — ошибка.

---

# 1. Общая концепция

## 1.1 Назначение системы

**AriaSignature** — это локальный Windows-сервис для:

* мониторинга состояния накопителей (HDD/SSD)
* управления архивацией баз **1С:Предприятие**
* предоставления данных через **локальный REST API** для интеграции с 1С

---

## 1.2 Ключевой принцип

Система = **Service-first архитектура**

UI не содержит логики.
Вся логика находится в Service.

---

## 1.3 Поддерживаемая платформа

* OS: Windows 10 / 11 (x64)
* Runtime: .NET 8+
* Работа только локально (без облака)

---

# 2. Архитектура системы

## 2.1 Компоненты (обязательно)

### 1. Core Service (Windows Service)

Назначение:

* сбор SMART
* управление задачами
* выполнение бэкапов
* запуск API
* планировщик

---

### 2. API Layer (внутри Service)

* ASP.NET Core (Self-hosted Kestrel)
* localhost only
* JSON

---

### 3. Desktop UI (WPF)

Назначение:

* конфигурация
* визуализация
* управление

---

## 2.2 Архитектурный стиль

* Clean Architecture
* Layered:

```
/domain
/application
/infrastructure
/api
/ui
```

---

## 2.3 Межпроцессное взаимодействие

UI ↔ Service:

* только через HTTP API
* НИКАКИХ прямых вызовов или shared memory

---

# 3. Технологический стек (фиксированный)

## Backend

* .NET 8
* ASP.NET Core
* Worker Service

## UI

* WPF (НЕ WinForms)

Причина:

* MVVM
* масштабируемость
* нормальная архитектура

## Data

* SQLite
* Dapper или EF Core (выбрать и придерживаться)

## Логирование

* Serilog

## Планировщик

* Quartz.NET

---

# 4. Хранение данных

## 4.1 БД: SQLite

## 4.2 Сущности

### Disks

* Id
* Model
* Serial
* Interface
* SizeTotal
* SizeFree

### SmartMetrics

* DiskId
* Temperature
* Health
* ReallocatedSectors
* PendingSectors
* Timestamp

### BackupJobs

* Id
* Name
* Type (File/MSSQL)
* Source
* Destination
* Schedule
* RetentionCount
* IsEnabled

### BackupLogs

* JobId
* Status
* StartTime
* EndTime
* FileSize
* Message

---

# 5. Модуль: Накопители

## 5.1 Требования

Поддержка:

* HDD
* SSD (SATA/NVMe)

---

## 5.2 Источник данных

* WMI
* при необходимости — P/Invoke

---

## 5.3 Метрики

Обязательно:

* Model
* Serial
* Interface
* Temperature
* Health (%)
* PowerOnHours
* PowerCycleCount
* Total Size
* Free Space

---

## 5.4 SMART анализ

Минимум:

* Reallocated Sectors
* Pending Sectors
* Uncorrectable Errors

---

## 5.5 Оценка состояния

```
OK        -> нет проблем
WARNING   -> есть деградация
CRITICAL  -> риск отказа
```

---

## 5.6 Обновление

* фоновые циклы (через Quartz)
* настраиваемый интервал

---

# 6. Модуль: Архивация 1С

## 6.1 Типы

### 1. File-based

* копирование `.1CD`
* использовать VSS (Volume Shadow Copy)

---

### 2. MSSQL

* BACKUP DATABASE → `.bak`

---

## 6.2 Планирование

* Quartz.NET
* cron-подобные выражения

---

## 6.3 Требования

* выполнение без UI
* retry при ошибках
* логирование

---

## 6.4 Retention policy

* хранить N копий
* удалять старые

---

# 7. REST API

## 7.1 Общие требования

* Base URL: `/api/v1`
* JSON only
* versioning обязателен

---

## 7.2 Эндпоинты

### Disks

* GET `/disks`
* GET `/disks/{id}`

---

### SMART

* GET `/disks/{id}/smart`

---

### Backups

* GET `/backups`
* POST `/backups`
* PUT `/backups/{id}`
* DELETE `/backups/{id}`
* POST `/backups/{id}/run`

---

### Logs

* GET `/backups/logs`

---

### System

* GET `/status`

---

## 7.3 Документация

* Swagger (обязательно)
* OpenAPI JSON

---

# 8. UI (WPF)

## 8.1 Архитектура

* MVVM
* Binding
* НИКАКОЙ логики в code-behind

---

## 8.2 Вкладки

### 1. Накопители

* список
* статус
* детализация

---

### 2. Архивация

* задачи
* журнал

---

### 3. Настройки

* API порт
* интервалы
* логирование

---

## 8.3 Требования

* zero-code UX
* строгий интерфейс
* без перегруженности

---

# 9. Установка и деплой

## 9.1 Инсталлятор

* WiX Toolset или Inno Setup

---

## 9.2 Обязательные действия

* установка Service
* регистрация автозапуска
* создание БД
* настройка портов

---

## 9.3 Service

* auto-start
* restart on failure

---

# 10. Git процесс

## 10.1 Структура

```
/src
  /service
  /api
  /core
  /ui
/docs
```

---

## 10.2 Ветки

* main
* develop
* feature/*

---

## 10.3 Коммиты

* Conventional Commits

---

# 11. Тестирование

## 11.1 Обязательно

* Unit tests
* API tests

---

## 11.2 Проверки

* SMART чтение
* backup выполнение
* scheduler

---

# 12. Ограничения (ЖЁСТКИЕ)

Запрещено:

* ❌ Делать MVP
* ❌ Упрощать архитектуру
* ❌ Убирать Windows Service
* ❌ Убирать API
* ❌ Хардкод
* ❌ Прямой доступ UI к данным

---

# 13. Порядок разработки (ОБЯЗАТЕЛЬНЫЙ)

1. Domain + модели
2. Service skeleton
3. SMART модуль
4. Backup модуль
5. API
6. DB интеграция
7. UI
8. Installer

---

# 14. Definition of Done

Система считается готовой, если:

* сервис стабильно работает 24/7
* API отвечает корректно
* backup выполняется по расписанию
* UI полностью управляет системой
* есть документация
* есть тесты

---

# 15. Инструкция для запуска разработки

После загрузки ТЗ:

```
1. Проанализируй архитектуру
2. Составь план разработки по шагам
3. Опиши структуру solution
4. Начни с Domain слоя
5. Жди подтверждения перед реализацией
```

---