import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiError, apiGet, apiSend } from "./api";

const GITHUB_REPO_URL = "https://github.com/erlkidd/AriaSignature";

type DiskRow = {
  id: string;
  model: string;
  serial: string;
  "interface": string;
  mediaType: string;
  sizeTotalBytes: number;
  sizeFreeBytes: number;
  sizeUsedBytes: number;
  ssdLifeRemainingPercent: number | null;
  temperatureCelsius: number | null;
  healthPercent: number | null;
  powerOnHours: number;
  powerCycleCount: number;
  reallocatedSectors: number;
  pendingSectors: number;
  uncorrectableErrors: number;
  smartCtlUsed: boolean;
  wmiUsed: boolean;
  storageReliabilityUsed: boolean;
  telemetryConfidence: number;
  telemetryDegradationReason: string;
  status: string;
  updatedAtUtc: string;
};

interface SmartRow {
  diskId: string;
  temperatureCelsius: number | null;
  healthPercent: number | null;
  reallocatedSectors: number;
  pendingSectors: number;
  uncorrectableErrors: number;
  status: string;
  timestampUtc: string;
}

interface BackupJob {
  id: string;
  name: string;
  type: string;
  source: string;
  destination: string;
  scheduleCron: string;
  retentionCount: number;
  isEnabled: boolean;
}

interface BackupLog {
  id: string;
  jobId: string;
  status: string;
  startTimeUtc: string;
  endTimeUtc: string | null;
  fileSizeBytes: number | null;
  message: string;
}

interface SettingsDto {
  apiPort: number;
  smartMonitoringCron: string;
  note: string;
}

type CreateJobField =
  | "jobName"
  | "fileSource"
  | "destFolder"
  | "msServer"
  | "msDb"
  | "msUser"
  | "msPassword";
type CreateJobFieldErrors = Partial<Record<CreateJobField, string>>;
type SmartScheduleMode = "interval" | "daily" | "custom";

const quartzDays = [
  { v: "SUN", label: "Воскресенье" },
  { v: "MON", label: "Понедельник" },
  { v: "TUE", label: "Вторник" },
  { v: "WED", label: "Среда" },
  { v: "THU", label: "Четверг" },
  { v: "FRI", label: "Пятница" },
  { v: "SAT", label: "Суббота" },
];

function formatHealthPercent(p: number | null | undefined): string {
  if (p == null) {
    return "н/д";
  }
  return `${p}%`;
}

function formatTempC(t: number | null | undefined): string {
  return t != null && t > 0 ? `${t} °C` : "—";
}

function formatPowerOnHours(h: number): string {
  return h > 0 ? `${h} ч` : "—";
}

function formatTelemetrySource(d: DiskRow): string {
  const parts: string[] = [];
  if (d.smartCtlUsed) parts.push("smartctl");
  if (d.storageReliabilityUsed) parts.push("StorageReliability");
  if (d.wmiUsed) parts.push("WMI");
  return parts.length > 0 ? parts.join(" + ") : "Источник не определен";
}

function parseSmartCron(cron: string): { mode: SmartScheduleMode; intervalMin: number; hour: number; minute: number } {
  const parts = cron.trim().split(/\s+/).filter(Boolean);
  if (parts.length >= 6) {
    const minuteRaw = parts[1] ?? "";
    const hourRaw = parts[2] ?? "";
    const dayRaw = parts[3] ?? "";
    const monthRaw = parts[4] ?? "";
    const dowRaw = parts[5] ?? "";

    const stepMatch = minuteRaw.match(/^\*\/(\d{1,2})$/);
    if (hourRaw === "*" && dayRaw === "*" && monthRaw === "*" && dowRaw === "?" && stepMatch) {
      const intervalMin = Number(stepMatch[1]);
      if ([5, 10, 15, 30, 60].includes(intervalMin)) {
        return { mode: "interval", intervalMin, hour: 2, minute: 0 };
      }
    }

    if (dayRaw === "*" && monthRaw === "*" && dowRaw === "?") {
      const minute = Number(minuteRaw);
      const hour = Number(hourRaw);
      if (Number.isFinite(minute) && Number.isFinite(hour) && minute >= 0 && minute <= 59 && hour >= 0 && hour <= 23) {
        return { mode: "daily", intervalMin: 60, hour, minute };
      }
    }
  }

  return { mode: "custom", intervalMin: 60, hour: 2, minute: 0 };
}

function formatWindowsServiceStatus(status?: string): string {
  switch ((status ?? "").toLowerCase()) {
    case "running":
      return "Запущена";
    case "stopped":
      return "Остановлена";
    case "paused":
      return "Приостановлена";
    case "startpending":
      return "Запускается";
    case "stoppending":
      return "Останавливается";
    case "pausepending":
      return "Приостанавливается";
    case "continuepending":
      return "Возобновляется";
    default:
      return status || "Неизвестно";
  }
}

function formatBytes(n: number): string {
  if (n >= 1_099_511_627_776) return `${(n / 1_099_511_627_776).toFixed(2)} ТиБ`;
  if (n >= 1_073_741_824) return `${(n / 1_073_741_824).toFixed(2)} ГиБ`;
  if (n >= 1_048_576) return `${(n / 1_048_576).toFixed(2)} МиБ`;
  return `${n} Б`;
}

function buildCron(
  preset: "daily" | "weekly" | "monthly",
  hour: number,
  minute: number,
  dayOfWeek: string,
  dayOfMonth: number
): string {
  const h = Math.min(23, Math.max(0, hour));
  const m = Math.min(59, Math.max(0, minute));
  if (preset === "daily") return `0 ${m} ${h} * * ?`;
  if (preset === "weekly") return `0 ${m} ${h} ? * ${dayOfWeek}`;
  const dom = Math.min(28, Math.max(1, dayOfMonth));
  return `0 ${m} ${h} ${dom} * ?`;
}

function cronToScheduleParts(cron: string): { periodicity: string; time: string } {
  const parts = cron.trim().split(/\s+/).filter(Boolean);
  if (parts.length < 6) {
    return { periodicity: "—", time: "—" };
  }
  const min = parts[1] ?? "0";
  const hour = parts[2] ?? "0";
  const day = parts[3];
  const month = parts[4];
  const dow = parts[5];
  const hm = (() => {
    const h = Math.min(23, Math.max(0, parseInt(hour, 10) || 0));
    const m = Math.min(59, Math.max(0, parseInt(min, 10) || 0));
    return `${h.toString().padStart(2, "0")}:${m.toString().padStart(2, "0")}`;
  })();
  if (day === "*" && month === "*" && (dow === "?" || dow === "*")) {
    return { periodicity: "Ежедневно", time: hm };
  }
  if (day === "?" && month === "*") {
    const d = quartzDays.find((x) => x.v === dow)?.label ?? dow;
    return { periodicity: `Еженедельно · ${d}`, time: hm };
  }
  if (month === "*" && day !== "?" && day !== "*" && (dow === "?" || dow === "*")) {
    return { periodicity: `Ежемесячно · ${day} число`, time: hm };
  }
  return { periodicity: "Quartz (свой вариант)", time: hm };
}

function jobTypeLabel(t: string): string {
  switch (t.toLowerCase()) {
    case "file":
      return "Файл .1CD";
    case "mssql":
      return "Резерв SQL Server";
    default:
      return t;
  }
}

function postToHost(payload: unknown) {
  const w = window as unknown as { chrome?: { webview?: { postMessage: (m: string) => void } } };
  if (w.chrome?.webview) {
    w.chrome.webview.postMessage(JSON.stringify(payload));
  }
}

type WindowsServiceStatus = {
  action?: string;
  ok?: boolean;
  status?: string;
  displayName?: string;
  error?: string;
};

export default function App() {
  const [tab, setTab] = useState<"disks" | "backup" | "settings" | "about">("disks");
  const [theme, setTheme] = useState<"light" | "dark">(() =>
    localStorage.getItem("aria-theme") === "dark" ? "dark" : "light"
  );
  const [status, setStatus] = useState("");
  const [error, setError] = useState<string | null>(null);

  const [disks, setDisks] = useState<DiskRow[]>([]);
  const [selectedDisk, setSelectedDisk] = useState<DiskRow | null>(null);
  const [smart, setSmart] = useState<SmartRow[]>([]);

  const [jobs, setJobs] = useState<BackupJob[]>([]);
  const [logs, setLogs] = useState<BackupLog[]>([]);
  const [logFilterStatus, setLogFilterStatus] = useState("");
  const [selectedJob, setSelectedJob] = useState<BackupJob | null>(null);
  const [backupTopTab, setBackupTopTab] = useState<"configure" | "active">("configure");
  const [backupBottomTab, setBackupBottomTab] = useState<"journal" | "backups">("journal");
  const [backupPageTab, setBackupPageTab] = useState<"tasks" | "history" | "new">("tasks");
  const [svcLine, setSvcLine] = useState("");
  const [windowsService, setWindowsService] = useState<WindowsServiceStatus | null>(null);

  const [settings, setSettings] = useState<SettingsDto | null>(null);
  const [launchAtStartup, setLaunchAtStartup] = useState(false);

  const [jobName, setJobName] = useState("");
  const [jobType, setJobType] = useState<"file" | "msSql">("file");
  const [fileSource, setFileSource] = useState("");
  const [destFolder, setDestFolder] = useState("");
  const [retention, setRetention] = useState(7);
  const [preset, setPreset] = useState<"daily" | "weekly" | "monthly">("daily");
  const [schHour, setSchHour] = useState(2);
  const [schMinute, setSchMinute] = useState(0);
  const [schDow, setSchDow] = useState("MON");
  const [schDom, setSchDom] = useState(1);
  const [advancedCron, setAdvancedCron] = useState("");
  const [useAdvancedCron, setUseAdvancedCron] = useState(false);

  const [msServer, setMsServer] = useState("localhost");
  const [msDb, setMsDb] = useState("");
  const [msAuth, setMsAuth] = useState<"sql" | "windows">("sql");
  const [msUser, setMsUser] = useState("");
  const [msPassword, setMsPassword] = useState("");
  const [createJobErrors, setCreateJobErrors] = useState<CreateJobFieldErrors>({});
  const [smartScheduleMode, setSmartScheduleMode] = useState<SmartScheduleMode>("interval");
  const [smartIntervalMin, setSmartIntervalMin] = useState(60);
  const [smartHour, setSmartHour] = useState(2);
  const [smartMinute, setSmartMinute] = useState(0);

  const cronValue = useMemo(() => {
    if (useAdvancedCron && advancedCron.trim()) return advancedCron.trim();
    return buildCron(preset, schHour, schMinute, schDow, schDom);
  }, [useAdvancedCron, advancedCron, preset, schHour, schMinute, schDow, schDom]);

  const backupJobsDisplayed = useMemo(
    () => (backupTopTab === "active" ? jobs.filter((j) => j.isEnabled) : jobs),
    [backupTopTab, jobs]
  );

  const backupSuccessLogs = useMemo(
    () => logs.filter((l) => l.status.toLowerCase() === "succeeded"),
    [logs]
  );

  const advancedCronHelp = useMemo(
    () =>
      "Формат Quartz — шесть полей через пробел: секунда, минута, час, день месяца, месяц, день недели (и при необходимости год). В заданные моменты планировщик ставит запуск задачи в очередь. Ниже — точная строка, которая будет сохранена.",
    []
  );

  const applyTheme = useCallback((t: "light" | "dark") => {
    document.documentElement.dataset.theme = t;
    localStorage.setItem("aria-theme", t);
    setTheme(t);
  }, []);

  useEffect(() => {
    if (!status || error) {
      return;
    }
    const t = window.setTimeout(() => setStatus(""), 5000);
    return () => window.clearTimeout(t);
  }, [status, error]);

  const toFriendlyError = (e: unknown): { message: string; fieldErrors?: CreateJobFieldErrors } => {
    const fallback = e instanceof Error ? e.message : String(e);
    if (!(e instanceof ApiError) || !e.body) {
      return { message: fallback };
    }

    try {
      const parsed = JSON.parse(e.body) as {
        title?: string;
        detail?: string;
        errors?: Record<string, string[]>;
      };

      const serverErrors = parsed.errors ?? {};
      const fieldErrors: CreateJobFieldErrors = {};
      const mapKey = (k: string): CreateJobField | null => {
        switch (k.trim().toLowerCase()) {
          case "source":
            return "fileSource";
          case "destination":
            return "destFolder";
          case "name":
            return "jobName";
          case "server":
            return "msServer";
          case "database":
            return "msDb";
          case "user":
            return "msUser";
          case "password":
            return "msPassword";
          default:
            return null;
        }
      };

      const humanLines: string[] = [];
      for (const [key, messages] of Object.entries(serverErrors)) {
        const field = mapKey(key);
        const message = messages?.[0] ?? "Некорректное значение";
        if (field) {
          fieldErrors[field] = message;
        }
        humanLines.push(`• ${message}`);
      }

      if (humanLines.length > 0) {
        return {
          message: `Проверьте заполнение формы:\n${humanLines.join("\n")}`,
          fieldErrors: fieldErrors,
        };
      }

      return { message: parsed.detail || parsed.title || e.message || fallback };
    } catch {
      return { message: e.body || e.message || fallback };
    }
  };

  const showErr = (e: unknown) => {
    const pretty = toFriendlyError(e);
    setError(pretty.message);
    setCreateJobErrors(pretty.fieldErrors ?? {});
    setStatus("");
  };

  const refreshDisks = useCallback(async () => {
    setError(null);
    try {
      const d = await apiSend<DiskRow[]>("/disks/refresh", "POST");
      setDisks(d);
      setStatus(`Накопители обновлены: ${d.length}`);
    } catch (e) {
      showErr(e);
    }
  }, []);

  const refreshJobs = useCallback(async () => {
    setError(null);
    try {
      const j = await apiGet<BackupJob[]>("/backups");
      setJobs(j);
    } catch (e) {
      showErr(e);
    }
  }, []);

  const refreshLogs = useCallback(async () => {
    setError(null);
    setStatus("");
    try {
      const q =
        logFilterStatus ? `?status=${encodeURIComponent(logFilterStatus)}` : "";
      const l = await apiGet<BackupLog[]>(`/backups/logs${q}`);
      setLogs(l);
    } catch (e) {
      showErr(e);
    }
  }, [logFilterStatus]);

  const refreshSettings = useCallback(async () => {
    setError(null);
    try {
      const s = await apiGet<SettingsDto>("/settings");
      setSettings(s);
    } catch (e) {
      showErr(e);
    }
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  useEffect(() => {
    const initialLoad = async () => {
      await Promise.all([refreshDisks(), refreshJobs(), refreshSettings()]);
      try {
        const l = await apiGet<BackupLog[]>("/backups/logs");
        setLogs(l);
      } catch (e) {
        showErr(e);
      }
    };
    void initialLoad();
    postToHost({ action: "getAutostart" });
  }, [refreshDisks, refreshJobs, refreshSettings]);

  useEffect(() => {
    if (tab !== "backup") {
      return;
    }
    let cancelled = false;
    const ping = async () => {
      try {
        const r = await fetch("/api/v1/status");
        if (!r.ok) {
          throw new Error(String(r.status));
        }
        const d = (await r.json()) as { status?: string; version?: string; timestampUtc?: string };
        if (cancelled) {
          return;
        }
        const ts = d.timestampUtc ? new Date(d.timestampUtc).toLocaleString() : "";
        setSvcLine(`Фоновый сервис: ${d.status ?? "OK"} · сборка ${d.version ?? "—"}${ts ? ` · ${ts}` : ""}`);
      } catch {
        if (!cancelled) {
          setSvcLine("Фоновый сервис: нет ответа API — проверьте службу AriaSignatureService.");
        }
      }
    };
    void ping();
    void refreshJobs();
    void refreshLogs();
    const id = window.setInterval(ping, 12_000);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, [tab, refreshJobs, refreshLogs]);

  useEffect(() => {
    if (tab !== "backup" || backupPageTab !== "history" || backupBottomTab !== "journal") {
      return;
    }
    void refreshLogs();
  }, [tab, backupPageTab, backupBottomTab, logFilterStatus, refreshLogs]);

  useEffect(() => {
    const chromeWebview = (
      window as unknown as {
        chrome?: { webview?: { addEventListener: (e: string, fn: (ev: { data: string }) => void) => void; removeEventListener: (e: string, fn: (ev: { data: string }) => void) => void } };
      }
    ).chrome?.webview;
    if (!chromeWebview) return;
    const fn = (ev: { data: string }) => {
      try {
        const data = JSON.parse(ev.data);
        if (data?.action === "autostart" && typeof data.enabled === "boolean") {
          setLaunchAtStartup(data.enabled);
        }
        if (data?.action === "pickedFile" && typeof data.path === "string") {
          setFileSource(data.path);
        }
        if (data?.action === "pickedFolder" && typeof data.path === "string") {
          setDestFolder(data.path);
        }
        if (data?.action === "windowsServiceStatus") {
          setWindowsService(data as WindowsServiceStatus);
        }
      } catch {
        /* ignore */
      }
    };
    chromeWebview.addEventListener("message", fn);
    return () => chromeWebview.removeEventListener("message", fn);
  }, []);

  useEffect(() => {
    if (tab !== "settings") {
      return;
    }
    postToHost({ action: "getWindowsServiceStatus" });
  }, [tab]);

  useEffect(() => {
    if (!settings?.smartMonitoringCron) {
      return;
    }
    const parsed = parseSmartCron(settings.smartMonitoringCron);
    setSmartScheduleMode(parsed.mode);
    setSmartIntervalMin(parsed.intervalMin);
    setSmartHour(parsed.hour);
    setSmartMinute(parsed.minute);
  }, [settings?.smartMonitoringCron]);

  useEffect(() => {
    if (!settings) {
      return;
    }

    if (smartScheduleMode === "interval") {
      const nextCron = `0 */${smartIntervalMin} * * * ?`;
      if (settings.smartMonitoringCron !== nextCron) {
        setSettings({ ...settings, smartMonitoringCron: nextCron });
      }
      return;
    }

    if (smartScheduleMode === "daily") {
      const nextCron = `0 ${smartMinute} ${smartHour} * * ?`;
      if (settings.smartMonitoringCron !== nextCron) {
        setSettings({ ...settings, smartMonitoringCron: nextCron });
      }
    }
  }, [settings, smartScheduleMode, smartIntervalMin, smartHour, smartMinute]);

  const loadSmart = async (disk: DiskRow) => {
    setSelectedDisk(disk);
    setError(null);
    try {
      const m = await apiGet<SmartRow[]>(`/disks/${disk.id}/smart`);
      setSmart(m);
    } catch (e) {
      showErr(e);
    }
  };

  const testMsSql = async () => {
    setError(null);
    setStatus("Проверка MSSQL…");
    try {
      await apiSend("/backups/test-mssql", "POST", {
        server: msServer,
        database: msDb,
        auth: msAuth,
        user: msUser,
        password: msPassword,
        trustServerCertificate: true,
      });
      setStatus("Подключение к MSSQL успешно.");
    } catch (e) {
      showErr(e);
    }
  };

  const createJob = async () => {
    setError(null);
    const nextErrors: CreateJobFieldErrors = {};

    if (!jobName.trim()) nextErrors.jobName = "Укажите название задачи.";
    if (!destFolder.trim()) nextErrors.destFolder = "Укажите папку для архивов.";
    if (jobType === "file" && !fileSource.trim()) {
      nextErrors.fileSource = "Укажите путь к файлу .1CD.";
    }
    if (jobType === "msSql") {
      if (!msServer.trim()) nextErrors.msServer = "Укажите сервер MSSQL.";
      if (!msDb.trim()) nextErrors.msDb = "Укажите имя базы MSSQL.";
      if (msAuth === "sql") {
        if (!msUser.trim()) nextErrors.msUser = "Укажите SQL-логин.";
        if (!msPassword.trim()) nextErrors.msPassword = "Укажите SQL-пароль.";
      }
    }

    if (Object.keys(nextErrors).length > 0) {
      setCreateJobErrors(nextErrors);
      setError("Заполните обязательные поля формы.");
      setStatus("");
      return;
    }

    setCreateJobErrors({});
    try {
      const body: Record<string, unknown> = {
        name: jobName || "Задача без имени",
        type: jobType,
        destination: destFolder,
        scheduleCron: cronValue,
        retentionCount: retention,
        isEnabled: true,
      };
      if (jobType === "file") body.source = fileSource;
      else {
        body.msSql = {
          server: msServer,
          database: msDb,
          auth: msAuth,
          user: msUser,
          password: msPassword,
          trustServerCertificate: true,
        };
        body.source = "";
      }
      await apiSend("/backups", "POST", body);
      setStatus("Задача создана.");
      await refreshJobs();
      setBackupPageTab("tasks");
    } catch (e) {
      showErr(e);
    }
  };

  const saveSelectedJob = async () => {
    if (!selectedJob) return;
    setError(null);
    try {
      const body: Record<string, unknown> = {
        name: selectedJob.name,
        type: selectedJob.type,
        source: selectedJob.source,
        destination: selectedJob.destination,
        scheduleCron: selectedJob.scheduleCron,
        retentionCount: selectedJob.retentionCount,
        isEnabled: selectedJob.isEnabled,
      };
      await apiSend(`/backups/${selectedJob.id}`, "PUT", body);
      setStatus("Задача сохранена.");
      await refreshJobs();
    } catch (e) {
      showErr(e);
    }
  };

  const runJob = async (id: string) => {
    setError(null);
    try {
      await apiSend(`/backups/${id}/run`, "POST");
      setStatus("Запуск архивации принят.");
      await refreshLogs();
    } catch (e) {
      showErr(e);
    }
  };

  const deleteJob = async (id: string) => {
    if (!confirm("Удалить задачу?")) return;
    setError(null);
    try {
      await fetch(`/api/v1/backups/${id}`, { method: "DELETE" });
      setStatus("Задача удалена.");
      setSelectedJob(null);
      await refreshJobs();
    } catch (e) {
      showErr(e);
    }
  };

  const toggleJobEnabled = async (j: BackupJob, enabled: boolean) => {
    setError(null);
    try {
      await apiSend(`/backups/${j.id}`, "PUT", {
        name: j.name,
        type: j.type,
        source: j.source,
        destination: j.destination,
        scheduleCron: j.scheduleCron,
        retentionCount: j.retentionCount,
        isEnabled: enabled,
      });
      await refreshJobs();
      setSelectedJob((prev) => (prev?.id === j.id ? { ...prev, isEnabled: enabled } : prev));
      setStatus(enabled ? "Задача включена." : "Задача отключена.");
    } catch (e) {
      showErr(e);
    }
  };

  const saveSettings = async () => {
    if (!settings) return;
    setError(null);
    try {
      await apiSend("/settings", "PUT", {
        apiPort: settings.apiPort,
        smartMonitoringCron: settings.smartMonitoringCron,
      });
      setStatus("Настройки записаны. При смене порта перезапустите службу.");
    } catch (e) {
      showErr(e);
    }
  };

  const statusClass = (s: string) => {
    const x = s.toLowerCase();
    return x === "critical" ? "pill bad" : x === "warning" ? "pill warn" : "pill ok";
  };

  return (
    <div className="app">
      <header className="header">
        <div className="header-brand">
          <img className="app-logo" src="./logo.png" width={72} height={72} alt="" />
          <div className="header-titles">
            <h1>AriaSignature</h1>
            <p className="subtitle">
              Диагностика накопителей и резервное копирование. Локальная панель управления.
            </p>
          </div>
        </div>
        <nav className="tabs">
          <button className={tab === "disks" ? "active" : ""} onClick={() => setTab("disks")}>
            Накопители
          </button>
          <button className={tab === "backup" ? "active" : ""} onClick={() => setTab("backup")}>
            Архивация
          </button>
          <button className={tab === "settings" ? "active" : ""} onClick={() => setTab("settings")}>
            Настройки
          </button>
          <button className={tab === "about" ? "active" : ""} onClick={() => setTab("about")}>
            О программе
          </button>
        </nav>
      </header>

      {error && (
        <div className="banner error">
          <strong>Ошибка</strong>
          <pre>{error}</pre>
          <button type="button" onClick={() => setError(null)}>
            Закрыть
          </button>
        </div>
      )}

      {status && !error && (
        <div className="banner ok banner-with-actions">
          <span className="banner-msg">{status}</span>
          <button type="button" className="btn-ghost" onClick={() => setStatus("")}>
            Скрыть
          </button>
        </div>
      )}

      {tab === "disks" && (
        <section className="panel">
          <div className="toolbar">
            <button type="button" onClick={() => void refreshDisks()}>
              Обновить данные
            </button>
            <span className="hint">
              Сбор телеметрии, выполнение архивации и журналирование выполняет служба AriaSignatureService. Панель предназначена
              для локального контроля состояния и администрирования.
            </span>
          </div>
          <div className="grid2">
            <div>
              <h2>Список</h2>
              <table className="data">
                <thead>
                  <tr>
                    <th>Состояние</th>
                    <th>Модель</th>
                    <th>Интерфейс</th>
                    <th>Здоровье</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {disks.map((d) => (
                    <tr key={d.id} className={selectedDisk?.id === d.id ? "sel" : ""}>
                      <td>
                        <span className={statusClass(d.status)}>{d.status}</span>
                      </td>
                      <td>{d.model}</td>
                      <td>
                        {d["interface"]} {d.mediaType ? `· ${d.mediaType}` : ""}
                      </td>
                      <td>{formatHealthPercent(d.healthPercent)}</td>
                      <td>
                        <button type="button" onClick={() => void loadSmart(d)}>
                          Детали
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="detail">
              <h2>Карточка</h2>
              {!selectedDisk && <p className="muted">Выберите диск.</p>}
              {selectedDisk && (
                <>
                  <dl className="kv">
                    <dt>Серийный номер</dt>
                    <dd>{selectedDisk.serial || "—"}</dd>
                    <dt>Объём</dt>
                    <dd>
                      Всего {formatBytes(selectedDisk.sizeTotalBytes)}, свободно {formatBytes(selectedDisk.sizeFreeBytes)},
                      занято {formatBytes(selectedDisk.sizeUsedBytes)}
                    </dd>
                    <dt>Температура</dt>
                    <dd>{formatTempC(selectedDisk.temperatureCelsius)}</dd>
                    <dt>Ресурс SSD</dt>
                    <dd>
                      {selectedDisk.ssdLifeRemainingPercent == null ? "н/д" : `${selectedDisk.ssdLifeRemainingPercent}%`}
                    </dd>
                    <dt>Наработка</dt>
                    <dd>
                      {formatPowerOnHours(selectedDisk.powerOnHours)}, включений {selectedDisk.powerCycleCount || "—"}
                    </dd>
                    <dt>SMART</dt>
                    <dd>
                      Reallocated {selectedDisk.reallocatedSectors}, Pending {selectedDisk.pendingSectors}, Uncorrectable{" "}
                      {selectedDisk.uncorrectableErrors}
                    </dd>
                    <dt>Источник телеметрии</dt>
                    <dd>{formatTelemetrySource(selectedDisk)}</dd>
                    <dt>Достоверность</dt>
                    <dd>{selectedDisk.telemetryConfidence}%</dd>
                    <dt>Диагностика</dt>
                    <dd>{selectedDisk.telemetryDegradationReason || "—"}</dd>
                  </dl>
                  <h3>История SMART (последние записи)</h3>
                  <table className="data compact">
                    <thead>
                      <tr>
                        <th>Время (UTC)</th>
                        <th>Темп.</th>
                        <th>Здоровье</th>
                      </tr>
                    </thead>
                    <tbody>
                      {smart.slice(0, 20).map((row, i) => (
                        <tr key={i}>
                          <td>{row.timestampUtc}</td>
                          <td>{formatTempC(row.temperatureCelsius)}</td>
                          <td>{formatHealthPercent(row.healthPercent)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </>
              )}
            </div>
          </div>
        </section>
      )}

      {tab === "backup" && (
        <section className="panel backup-workspace">
          <div className="backup-main-tabs">
            <button
              type="button"
              className={backupPageTab === "tasks" ? "active" : ""}
              onClick={() => setBackupPageTab("tasks")}
            >
              Задачи
            </button>
            <button
              type="button"
              className={backupPageTab === "history" ? "active" : ""}
              onClick={() => setBackupPageTab("history")}
            >
              Журнал и копии
            </button>
            <button
              type="button"
              className={backupPageTab === "new" ? "active" : ""}
              onClick={() => setBackupPageTab("new")}
            >
              Создать задачу
            </button>
          </div>

          {backupPageTab === "tasks" && (
          <div className="backup-split-top">
            <div className="backup-subtabs">
              <button
                type="button"
                className={backupTopTab === "configure" ? "active" : ""}
                onClick={() => setBackupTopTab("configure")}
              >
                Настройка задач
              </button>
              <button
                type="button"
                className={backupTopTab === "active" ? "active" : ""}
                onClick={() => setBackupTopTab("active")}
              >
                Активные задачи
              </button>
            </div>
            <div className="toolbar backup-toolbar">
              <button type="button" className="secondary" onClick={() => void refreshJobs()}>
                Обновить список
              </button>
            </div>
            <div className="table-wrap">
              <table className="data backup-jobs-table">
                <thead>
                  <tr>
                    <th>Наименование</th>
                    <th>Вкл</th>
                    <th>Вид задачи</th>
                    <th>Периодичность</th>
                    <th>Время</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {backupJobsDisplayed.map((j) => {
                    const { periodicity, time } = cronToScheduleParts(j.scheduleCron);
                    return (
                      <tr key={j.id} className={`backup-job-row ${selectedJob?.id === j.id ? "sel" : ""}`}>
                        <td>
                          <span className="task-name-cell">📁 {j.name}</span>
                        </td>
                        <td onClick={(e) => e.stopPropagation()}>
                          <input
                            type="checkbox"
                            checked={j.isEnabled}
                            onChange={(e) => void toggleJobEnabled(j, e.target.checked)}
                            aria-label={`Включена задача ${j.name}`}
                          />
                        </td>
                        <td>{jobTypeLabel(j.type)}</td>
                        <td>{periodicity}</td>
                        <td className="mono">{time}</td>
                        <td className="row-actions" onClick={(e) => e.stopPropagation()}>
                          <button type="button" className="secondary" onClick={() => setSelectedJob({ ...j })}>
                            Редактировать
                          </button>
                          <button type="button" onClick={() => void runJob(j.id)}>
                            Запуск
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {selectedJob && (
              <details className="job-editor-details">
                <summary>Редактирование: {selectedJob.name}</summary>
                <div className="job-editor-body box backup-edit-box">
                <label>
                  Имя
                  <input
                    value={selectedJob.name}
                    onChange={(e) => setSelectedJob({ ...selectedJob, name: e.target.value })}
                  />
                </label>
                <label>
                  Источник (путь или строка подключения)
                  <textarea
                    value={selectedJob.source}
                    onChange={(e) => setSelectedJob({ ...selectedJob, source: e.target.value })}
                    rows={3}
                  />
                </label>
                <label>
                  Папка архивов
                  <input
                    value={selectedJob.destination}
                    onChange={(e) => setSelectedJob({ ...selectedJob, destination: e.target.value })}
                  />
                </label>
                <label>
                  Cron
                  <input
                    value={selectedJob.scheduleCron}
                    onChange={(e) => setSelectedJob({ ...selectedJob, scheduleCron: e.target.value })}
                  />
                </label>
                <label>
                  Копий
                  <input
                    type="number"
                    value={selectedJob.retentionCount}
                    onChange={(e) => setSelectedJob({ ...selectedJob, retentionCount: Number(e.target.value) })}
                  />
                </label>
                <label className="check">
                  <input
                    type="checkbox"
                    checked={selectedJob.isEnabled}
                    onChange={(e) => setSelectedJob({ ...selectedJob, isEnabled: e.target.checked })}
                  />
                  Включена
                </label>
                <div className="row">
                  <button type="button" onClick={() => void saveSelectedJob()}>
                    Сохранить
                  </button>
                  <button type="button" onClick={() => void runJob(selectedJob.id)}>
                    Запустить
                  </button>
                  <button type="button" className="danger" onClick={() => void deleteJob(selectedJob.id)}>
                    Удалить
                  </button>
                </div>
                </div>
              </details>
            )}
          </div>
          )}

          {backupPageTab === "history" && (
          <div className="backup-split-bottom">
            <div className="backup-subtabs backup-bottom-subtabs">
              <button
                type="button"
                className={backupBottomTab === "journal" ? "active" : ""}
                onClick={() => setBackupBottomTab("journal")}
              >
                Журнал задач
              </button>
              <button
                type="button"
                className={backupBottomTab === "backups" ? "active" : ""}
                onClick={() => setBackupBottomTab("backups")}
              >
                Бэкапы
              </button>
            </div>

            {backupBottomTab === "journal" && (
              <>
                <div className="toolbar">
                  <select value={logFilterStatus} onChange={(e) => setLogFilterStatus(e.target.value)}>
                    <option value="">Все статусы</option>
                    <option value="Succeeded">Успех</option>
                    <option value="Failed">Ошибка</option>
                  </select>
                  <button type="button" onClick={() => void refreshLogs()}>
                    Обновить журнал
                  </button>
                </div>
                <div className="table-wrap">
                  <table className="data compact">
                    <thead>
                      <tr>
                        <th>Дата</th>
                        <th>Задача</th>
                        <th>Статус</th>
                        <th>Размер</th>
                        <th>Результат</th>
                      </tr>
                    </thead>
                    <tbody>
                      {logs.map((l) => (
                        <tr key={l.id}>
                          <td className="mono small">{new Date(l.startTimeUtc).toLocaleString()}</td>
                          <td>{jobs.find((x) => x.id === l.jobId)?.name ?? l.jobId}</td>
                          <td>{l.status}</td>
                          <td>{l.fileSizeBytes != null ? formatBytes(l.fileSizeBytes) : "—"}</td>
                          <td className="msg">{l.message}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}

            {backupBottomTab === "backups" && (
              <>
                <p className="hint">
                  Успешные архивации из журнала. Путь к файлу обычно указан в сообщении или в папке назначения задачи.
                </p>
                <div className="table-wrap">
                  <table className="data compact">
                    <thead>
                      <tr>
                        <th>Дата</th>
                        <th>Задача</th>
                        <th>Размер</th>
                        <th>Сообщение</th>
                      </tr>
                    </thead>
                    <tbody>
                      {backupSuccessLogs.map((l) => (
                        <tr key={l.id}>
                          <td className="mono small">{new Date(l.startTimeUtc).toLocaleString()}</td>
                          <td>{jobs.find((x) => x.id === l.jobId)?.name ?? l.jobId}</td>
                          <td>{l.fileSizeBytes != null ? formatBytes(l.fileSizeBytes) : "—"}</td>
                          <td className="msg">{l.message}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </div>
          )}

          {backupPageTab === "new" && (
            <div className="new-task-page">
              <h2 className="new-task-page-title">Новая задача архивации</h2>
              <div className="new-task-body">
                <label>
                  Имя
                  <input
                    className={createJobErrors.jobName ? "invalid-input" : ""}
                    value={jobName}
                    onChange={(e) => {
                      setJobName(e.target.value);
                      setCreateJobErrors((prev) => ({ ...prev, jobName: undefined }));
                    }}
                    placeholder="Имя задачи"
                  />
                  {createJobErrors.jobName && <span className="field-error">{createJobErrors.jobName}</span>}
                </label>
                <label>
                  Тип
                  <select value={jobType} onChange={(e) => setJobType(e.target.value as "file" | "msSql")}>
                    <option value="file">Файловая база (.1CD)</option>
                    <option value="msSql">Microsoft SQL Server</option>
                  </select>
                </label>
                {jobType === "file" ? (
                  <>
                    <label>
                      Путь к файлу .1CD
                      <div className="row" style={{ alignItems: "stretch" }}>
                        <input
                          className={createJobErrors.fileSource ? "invalid-input" : ""}
                          style={{ flex: 1 }}
                          value={fileSource}
                          onChange={(e) => {
                            setFileSource(e.target.value);
                            setCreateJobErrors((prev) => ({ ...prev, fileSource: undefined }));
                          }}
                          placeholder="D:\Base\1Cv8.1CD"
                        />
                        <button type="button" className="secondary" onClick={() => postToHost({ action: "pickFile" })}>
                          Обзор…
                        </button>
                      </div>
                    </label>
                    {createJobErrors.fileSource && <span className="field-error">{createJobErrors.fileSource}</span>}
                  </>
                ) : (
                  <div className="box">
                    <p className="hint">
                      <strong>MSSQL:</strong> при режиме «Windows» резервная копия выполняется от имени{" "}
                      <strong>учётной записи службы</strong> Windows (services.msc → AriaSignatureService). У этой учётки
                      должны быть права на базу. При «SQL» учётные данные сохраняются в локальной SQLite приложения в
                      составе строки подключения задачи (ограничьте доступ к каталогу установки).
                    </p>
                    <label>
                      Сервер
                      <input
                        className={createJobErrors.msServer ? "invalid-input" : ""}
                        value={msServer}
                        onChange={(e) => {
                          setMsServer(e.target.value);
                          setCreateJobErrors((prev) => ({ ...prev, msServer: undefined }));
                        }}
                      />
                      {createJobErrors.msServer && <span className="field-error">{createJobErrors.msServer}</span>}
                    </label>
                    <label>
                      База данных
                      <input
                        className={createJobErrors.msDb ? "invalid-input" : ""}
                        value={msDb}
                        onChange={(e) => {
                          setMsDb(e.target.value);
                          setCreateJobErrors((prev) => ({ ...prev, msDb: undefined }));
                        }}
                      />
                      {createJobErrors.msDb && <span className="field-error">{createJobErrors.msDb}</span>}
                    </label>
                    <label>
                      Аутентификация
                      <select value={msAuth} onChange={(e) => setMsAuth(e.target.value as "sql" | "windows")}>
                        <option value="sql">SQL (логин / пароль)</option>
                        <option value="windows">Windows (Integrated)</option>
                      </select>
                    </label>
                    {msAuth === "sql" && (
                      <>
                        <label>
                          Логин
                          <input
                            className={createJobErrors.msUser ? "invalid-input" : ""}
                            value={msUser}
                            onChange={(e) => {
                              setMsUser(e.target.value);
                              setCreateJobErrors((prev) => ({ ...prev, msUser: undefined }));
                            }}
                            autoComplete="off"
                          />
                          {createJobErrors.msUser && <span className="field-error">{createJobErrors.msUser}</span>}
                        </label>
                        <label>
                          Пароль
                          <input
                            type="password"
                            className={createJobErrors.msPassword ? "invalid-input" : ""}
                            value={msPassword}
                            onChange={(e) => {
                              setMsPassword(e.target.value);
                              setCreateJobErrors((prev) => ({ ...prev, msPassword: undefined }));
                            }}
                            autoComplete="off"
                          />
                          {createJobErrors.msPassword && <span className="field-error">{createJobErrors.msPassword}</span>}
                        </label>
                      </>
                    )}
                    <button type="button" className="secondary" onClick={() => void testMsSql()}>
                      Проверить подключение
                    </button>
                  </div>
                )}
                <label>
                  Папка для архивов (полный путь)
                  <div className="row" style={{ alignItems: "stretch" }}>
                    <input
                      className={createJobErrors.destFolder ? "invalid-input" : ""}
                      style={{ flex: 1 }}
                      value={destFolder}
                      onChange={(e) => {
                        setDestFolder(e.target.value);
                        setCreateJobErrors((prev) => ({ ...prev, destFolder: undefined }));
                      }}
                      placeholder="D:\Backups\1C"
                    />
                    <button type="button" className="secondary" onClick={() => postToHost({ action: "pickFolder" })}>
                      Папка…
                    </button>
                  </div>
                </label>
                {createJobErrors.destFolder && <span className="field-error">{createJobErrors.destFolder}</span>}
                <label>
                  Хранить копий (ротация)
                  <input type="number" min={1} value={retention} onChange={(e) => setRetention(Number(e.target.value))} />
                </label>

                <h3>Расписание</h3>
                <label className="check">
                  <input type="checkbox" checked={useAdvancedCron} onChange={(e) => setUseAdvancedCron(e.target.checked)} />
                  Расширенный режим (cron Quartz)
                </label>
                {!useAdvancedCron ? (
                  <>
                    <label>
                      Периодичность
                      <select value={preset} onChange={(e) => setPreset(e.target.value as typeof preset)}>
                        <option value="daily">Ежедневно</option>
                        <option value="weekly">Еженедельно</option>
                        <option value="monthly">Ежемесячно</option>
                      </select>
                    </label>
                    <div className="row">
                      <label>
                        Час
                        <input type="number" min={0} max={23} value={schHour} onChange={(e) => setSchHour(+e.target.value)} />
                      </label>
                      <label>
                        Минута
                        <input
                          type="number"
                          min={0}
                          max={59}
                          value={schMinute}
                          onChange={(e) => setSchMinute(+e.target.value)}
                        />
                      </label>
                    </div>
                    {preset === "weekly" && (
                      <label>
                        День недели
                        <select value={schDow} onChange={(e) => setSchDow(e.target.value)}>
                          {quartzDays.map((d) => (
                            <option key={d.v} value={d.v}>
                              {d.label}
                            </option>
                          ))}
                        </select>
                      </label>
                    )}
                    {preset === "monthly" && (
                      <label>
                        Число месяца
                        <input type="number" min={1} max={28} value={schDom} onChange={(e) => setSchDom(+e.target.value)} />
                      </label>
                    )}
                  </>
                ) : (
                  <label>
                    Cron
                    <input value={advancedCron} onChange={(e) => setAdvancedCron(e.target.value)} placeholder="0 0 2 * * ?" />
                  </label>
                )}
                {useAdvancedCron && <p className="cron-hint">{advancedCronHelp}</p>}
                <p className="muted mono small">Quartz: {cronValue}</p>
                <button type="button" onClick={() => void createJob()}>
                  Создать задачу
                </button>
              </div>
            </div>
          )}

          <div
            className={`status-bar-effector${svcLine.includes("нет ответа") ? " err" : ""}`}
            role="status"
            aria-live="polite"
          >
            {svcLine || "Состояние API: ожидание…"}
          </div>
        </section>
      )}

      {tab === "about" && (
        <section className="panel about-page">
          <div className="about-brand">
            <img className="about-logo" src="./logo.png" width={120} height={120} alt="" />
            <h2>AriaSignature</h2>
            <p className="hint">
              Локальная панель для мониторинга накопителей и резервного копирования баз 1С. Служба Windows предоставляет
              HTTP API для интеграций.
            </p>
            <p>
              <a href={GITHUB_REPO_URL} target="_blank" rel="noreferrer">
                Исходный код на GitHub
              </a>
            </p>
            <p className="muted">
              Лицензия:{" "}
              <a href={`${GITHUB_REPO_URL}/blob/main/LICENSE`} target="_blank" rel="noreferrer">
                MIT
              </a>{" "}
              — свободное использование с сохранением уведомления об авторских правах.
            </p>
            <p className="about-author muted">Автор: Arthur Barmine</p>
          </div>
        </section>
      )}

      {tab === "settings" && settings && (
        <section className="panel">
          <h2>Служба Windows</h2>
          <div className="box service-control-box">
            <p className="hint">
              Управление выполняется оболочкой приложения (нужны права администратора). После остановки службы панель может
              временно потерять связь с API до следующего запуска.
            </p>
            <p>
              <strong>AriaSignatureService:</strong>{" "}
              {windowsService?.ok === false ? (
                <span className="pill bad">недоступна ({windowsService.error ?? "ошибка"})</span>
              ) : windowsService?.status === "Running" ? (
                <span className="pill ok">запущена</span>
              ) : windowsService?.status ? (
                <span className="pill warn">{formatWindowsServiceStatus(windowsService.status)}</span>
              ) : (
                <span className="muted">загрузка…</span>
              )}
            </p>
            <div className="row service-control-actions">
              <button type="button" className="secondary" onClick={() => postToHost({ action: "getWindowsServiceStatus" })}>
                Обновить статус
              </button>
              <button type="button" onClick={() => postToHost({ action: "controlWindowsService", command: "start" })}>
                Запуск
              </button>
              <button type="button" className="secondary" onClick={() => postToHost({ action: "controlWindowsService", command: "stop" })}>
                Остановить
              </button>
              <button type="button" onClick={() => postToHost({ action: "controlWindowsService", command: "restart" })}>
                Перезапуск
              </button>
            </div>
          </div>

          <h2>Параметры службы</h2>
          <p className="hint">{settings.note}</p>
          <label>
            Порт API (localhost)
            <input
              type="number"
              value={settings.apiPort}
              onChange={(e) => setSettings({ ...settings, apiPort: Number(e.target.value) })}
            />
          </label>
          <label>
            Обновление SMART
            <select
              value={smartScheduleMode}
              onChange={(e) => setSmartScheduleMode(e.target.value as SmartScheduleMode)}
            >
              <option value="interval">Каждые N минут</option>
              <option value="daily">Ежедневно в указанное время</option>
              <option value="custom">Расширенный режим (Quartz)</option>
            </select>
          </label>
          {smartScheduleMode === "interval" && (
            <label>
              Интервал
              <select
                value={smartIntervalMin}
                onChange={(e) => {
                  const next = Number(e.target.value);
                  setSmartIntervalMin(next);
                  setSettings({ ...settings, smartMonitoringCron: `0 */${next} * * * ?` });
                }}
              >
                <option value={5}>Каждые 5 минут</option>
                <option value={10}>Каждые 10 минут</option>
                <option value={15}>Каждые 15 минут</option>
                <option value={30}>Каждые 30 минут</option>
                <option value={60}>Каждый час</option>
              </select>
            </label>
          )}
          {smartScheduleMode === "daily" && (
            <div className="row">
              <label>
                Час
                <input
                  type="number"
                  min={0}
                  max={23}
                  value={smartHour}
                  onChange={(e) => {
                    const h = Math.max(0, Math.min(23, Number(e.target.value) || 0));
                    setSmartHour(h);
                    setSettings({ ...settings, smartMonitoringCron: `0 ${smartMinute} ${h} * * ?` });
                  }}
                />
              </label>
              <label>
                Минута
                <input
                  type="number"
                  min={0}
                  max={59}
                  value={smartMinute}
                  onChange={(e) => {
                    const m = Math.max(0, Math.min(59, Number(e.target.value) || 0));
                    setSmartMinute(m);
                    setSettings({ ...settings, smartMonitoringCron: `0 ${m} ${smartHour} * * ?` });
                  }}
                />
              </label>
            </div>
          )}
          {smartScheduleMode === "custom" && (
            <>
              <label>
                Cron Quartz
                <input
                  value={settings.smartMonitoringCron}
                  onChange={(e) => setSettings({ ...settings, smartMonitoringCron: e.target.value })}
                  placeholder="0 */15 * * * ?"
                />
              </label>
              <p className="hint">
                Формат: <span className="mono">секунда минута час день_месяца месяц день_недели</span>. Пример:{" "}
                <span className="mono">0 */15 * * * ?</span> — каждые 15 минут.
              </p>
            </>
          )}
          {smartScheduleMode !== "custom" && (
            <p className="hint">
              Текущий cron: <span className="mono">{settings.smartMonitoringCron}</span>
            </p>
          )}
          <button type="button" onClick={() => void saveSettings()}>
            Сохранить в базу настроек
          </button>

          <h2>Оформление панели</h2>
          <label>
            Тема
            <select value={theme} onChange={(e) => applyTheme(e.target.value as "light" | "dark")}>
              <option value="light">Светлая</option>
              <option value="dark">Тёмная</option>
            </select>
          </label>

          <h2>Автозапуск панели</h2>
          <label className="check">
            <input
              type="checkbox"
              checked={launchAtStartup}
              onChange={(e) => {
                setLaunchAtStartup(e.target.checked);
                postToHost({ action: "setAutostart", enabled: e.target.checked });
              }}
            />
            Запускать AriaSignature при входе в Windows
          </label>
          <p className="hint">Настройка применяется через оболочку Windows (реестр текущего пользователя).</p>
        </section>
      )}

      <footer className="footer">
        <span>AriaSignature v0.2.2</span>
        <a href="/swagger" target="_blank" rel="noreferrer">
          Swagger / OpenAPI
        </a>
      </footer>
    </div>
  );
}
