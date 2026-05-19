# OpenIntegrations + Melezh Taxonomy Map

This map is built from:

- `openintegrations-docs-mirror/docs/ru/md`
- `openintegrations-docs-mirror/docs/en/md`
- `Melezh/docs/ru/md`
- `Melezh/docs/en/md`

## OInt Top-Level Sections

- `Start`
- `Addons`
- `Instructions` (section index pages)
- Service/API sections:
  - `Telegram`, `Bitrix24`, `CDEK`, `VK`, `VKTeams`, `Viber`
  - `GreenAPI`, `GreenMax`
  - `YandexDisk`, `YandexMetrika`
  - `GoogleCalendar`, `GoogleDrive`, `GoogleSheets`
  - `Notion`, `Airtable`, `Slack`, `Dropbox`, `Neocities`
  - `S3`
  - `PostgreSQL`, `SQLite`, `MSSQL`, `MySQL`, `ClickHouse`, `MongoDB`
  - `HTTP`, `TCP`, `WebSocket`, `GRPC`, `ZeroMQ`, `SSH`, `SFTP`, `FTP`, `RCON`, `RSS`
  - `OpenAI`, `Ollama`, `ReportPortal`

## Melezh Top-Level Sections

- `Melezh` (overview)
- `Start`
  - `General-provisions`, `Installation`, `First-start`, `Logging`, `Debugging`, `OInt-and-extensions`
- `Console-Interface`
  - `Argument-setting`
  - `Handlers-configuration`
  - `Projects-setup`
  - `Scheduled-tasks`
- `Web-Interface`
  - `Getting-started`, `Handlers-panel`, `Settings-panel`, `Logs-panel`, `Extensions-panel`

## Target Artifact Mapping

- Skills:
  - `skills/1с-oint-skills/*`
  - `skills/1c-melezh-skills/*`
- Developer docs:
  - `md-docs/oint/*`
  - `md-docs/melezh/*`

## Coverage Strategy

- Full-by-sections coverage is implemented at section/service level.
- Each generated skill references one or multiple canonical source sections.
- Each MDX doc page includes RU + EN guidance and links to related sections.
