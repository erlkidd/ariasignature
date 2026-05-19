import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiError, apiGet, apiSend } from "./api";

const GITHUB_REPO_URL = "https://github.com/erlkidd/AriaSignature";
const UI_BUILD_VERSION = "1.1.0";

const logoSrc = `./logo.png?v=${encodeURIComponent(__LOGO_CACHE_BUST__)}`;

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
  /** all — доступ по IP/VPN; loopback — только с этой машины */
  apiBind: string;
  /** Токен для Authorization: Bearer / X-Aria-Api-Key с других узлов; пусто — без проверки заголовка */
  apiSharedSecret: string;
  smartMonitoringCron: string;
  note: string;
  outboundSyncEnabled: boolean;
  outboundSyncUrl: string;
  outboundSyncCron: string;
  melezhEnabled: boolean;
  melezhPort: number;
  melezhUiUrl: string;
  melezhServiceName: string;
  melezhServiceStatus?: string | null;
  melezhRunning: boolean;
}

interface NetworkAddressInfoDto {
  interfaceDescription?: string | null;
  address: string;
  family: string;
}

interface SystemInfoDto {
  collectedAtUtc: string;
  agentVersion: string;
  hostName: string;
  dnsHostName?: string | null;
  networkAddresses: NetworkAddressInfoDto[];
  osCaption?: string | null;
  osVersion?: string | null;
  processorName?: string | null;
  logicalProcessors?: number | null;
  totalRamBytes?: number | null;
  availableRamBytes?: number | null;
  videoControllers: string[];
}

interface ServiceStatusDto {
  status?: string;
  version?: string;
  timestampUtc?: string;
}

interface ClearSmartResponse {
  cleared?: boolean;
  scope?: string;
  deleted?: number;
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
const archivePathRegex = /([A-Za-z]:\\[^<>:"|?*\r\n]+?\.(?:rar|zip|7z|bak|1cd))/i;

const quartzDays = [
  { v: "SUN", label: "Воскресенье" },
  { v: "MON", label: "Понедельник" },
  { v: "TUE", label: "Вторник" },
  { v: "WED", label: "Среда" },
  { v: "THU", label: "Четверг" },
  { v: "FRI", label: "Пятница" },
  { v: "SAT", label: "Суббота" },
];

function formatSsdLifePercent(d: Pick<DiskRow, "ssdLifeRemainingPercent" | "healthPercent" | "mediaType" | "interface">): string {
  const media = (d.mediaType ?? "").toUpperCase();
  const iface = (d["interface"] ?? "").toUpperCase();
  if (media.includes("HDD") || media.includes("ЖЕСТК")) {
    return "—";
  }
  if (d.ssdLifeRemainingPercent != null) {
    return `${d.ssdLifeRemainingPercent}%`;
  }
  const isFlash = media.includes("SSD") || media.includes("NVME") || media.includes("FLASH") || iface.includes("NVME");
  if (isFlash && d.healthPercent != null) {
    return `${d.healthPercent}%`;
  }
  return "н/д";
}

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
  if (!(h > 0)) {
    return "—";
  }
  const days = Math.floor(h / 24);
  if (days <= 0) {
    return `${h} ч`;
  }
  const restHours = h % 24;
  return `${days} дн${restHours > 0 ? ` ${restHours} ч` : ""}`;
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
    const sec = parts[0] ?? "";
    const minuteRaw = parts[1] ?? "";
    const hourRaw = parts[2] ?? "";
    const dayRaw = parts[3] ?? "";
    const monthRaw = parts[4] ?? "";
    const dowRaw = parts[5] ?? "";

    // Каждый час (верх часа): 0 0 * * * ?
    if (sec === "0" && minuteRaw === "0" && hourRaw === "*" && dayRaw === "*" && monthRaw === "*" && dowRaw === "?") {
      return { mode: "interval", intervalMin: 60, hour: 2, minute: 0 };
    }

    // Каждые N минут: 0 0/N * * * ?
    const slashStartMin = minuteRaw.match(/^0\/(\d{1,2})$/);
    if (
      sec === "0" &&
      slashStartMin &&
      hourRaw === "*" &&
      dayRaw === "*" &&
      monthRaw === "*" &&
      dowRaw === "?"
    ) {
      const n = Number(slashStartMin[1]);
      if (n >= 1 && n <= 59) {
        return { mode: "interval", intervalMin: n, hour: 2, minute: 0 };
      }
    }

    // Устаревший вид минутного поля */N (в т.ч. минутный шаг из старых версий)
    const stepMatch = minuteRaw.match(/^\*\/(\d{1,2})$/);
    if (hourRaw === "*" && dayRaw === "*" && monthRaw === "*" && dowRaw === "?" && stepMatch) {
      const intervalMin = Number(stepMatch[1]);
      if (intervalMin >= 1 && intervalMin <= 59) {
        return { mode: "interval", intervalMin, hour: 2, minute: 0 };
      }
    }

    // Ежедневно: числовые минута и час
    if (dayRaw === "*" && monthRaw === "*" && dowRaw === "?" && hourRaw !== "*") {
      const minute = Number(minuteRaw);
      const hour = Number(hourRaw);
      if (Number.isFinite(minute) && Number.isFinite(hour) && minute >= 0 && minute <= 59 && hour >= 0 && hour <= 23) {
        return { mode: "daily", intervalMin: 60, hour, minute };
      }
    }
  }

  return { mode: "custom", intervalMin: 60, hour: 2, minute: 0 };
}

function buildSmartCron(mode: SmartScheduleMode, intervalMin: number, hour: number, minute: number, customCron: string): string {
  if (mode === "interval") {
    const n = Math.max(1, Math.floor(intervalMin));
    if (n >= 60) {
      return "0 0 * * * ?";
    }
    return `0 0/${n} * * * ?`;
  }
  if (mode === "daily") {
    return `0 ${minute} ${hour} * * ?`;
  }
  return customCron.trim();
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

type ParsedBackupSchedule =
  | { kind: "simple"; preset: "daily" | "weekly" | "monthly"; hour: number; minute: number; dow: string; dom: number }
  | { kind: "advanced"; cron: string };

/** Распознаёт cron, совпадающий с пресетами формы создания задачи; иначе — расширенный Quartz. */
function parseBackupScheduleEditor(cronRaw: string): ParsedBackupSchedule {
  const cron = (cronRaw ?? "").trim();
  const parts = cron.split(/\s+/).filter(Boolean);
  if (parts.length < 6) {
    return { kind: "advanced", cron: cron || "0 0 2 * * ?" };
  }
  const sec = parts[0] ?? "";
  const minuteRaw = parts[1] ?? "";
  const hourRaw = parts[2] ?? "";
  const day = parts[3] ?? "";
  const month = parts[4] ?? "";
  const dow = parts[5] ?? "";

  if (sec !== "0") {
    return { kind: "advanced", cron };
  }

  const minute = Number(minuteRaw);
  const hour = Number(hourRaw);
  if (!Number.isFinite(minute) || !Number.isFinite(hour) || minute < 0 || minute > 59 || hour < 0 || hour > 23) {
    return { kind: "advanced", cron };
  }

  if (day === "*" && month === "*" && dow === "?") {
    return { kind: "simple", preset: "daily", hour, minute, dow: "MON", dom: 1 };
  }

  if (day === "?" && month === "*" && dow !== "?" && dow !== "*") {
    const known = quartzDays.some((x) => x.v === dow);
    if (known) {
      return { kind: "simple", preset: "weekly", hour, minute, dow, dom: 1 };
    }
    return { kind: "advanced", cron };
  }

  const domNum = Number(day);
  if (
    month === "*" &&
    (dow === "?" || dow === "*") &&
    Number.isFinite(domNum) &&
    domNum >= 1 &&
    domNum <= 28 &&
    day !== "*" &&
    day !== "?"
  ) {
    return { kind: "simple", preset: "monthly", hour, minute, dow: "MON", dom: domNum };
  }

  return { kind: "advanced", cron };
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

function formatBackupLogStatus(status: string): string {
  const raw = (status ?? "").trim();
  const s = raw.toLowerCase().replace(/\s+/g, "");
  switch (s) {
    case "succeeded":
    case "success":
      return "Успех";
    case "failed":
    case "failure":
      return "Ошибка";
    case "running":
    case "inprogress":
      return "Выполняется";
    case "pending":
      return "Ожидает";
    case "0":
      return "Ожидает";
    case "1":
      return "Выполняется";
    case "2":
      return "Успех";
    case "3":
      return "Ошибка";
    case "cancelled":
    case "canceled":
      return "Отменено";
    default: {
      if (!s) return "—";
      if (/[а-яё]/i.test(raw)) return raw;
      return "Неизвестно";
    }
  }
}

function extractFolderFromLogMessage(message: string): string | null {
  const match = message?.match(archivePathRegex);
  if (!match?.[1]) {
    return null;
  }

  const filePath = match[1];
  const idx = Math.max(filePath.lastIndexOf("\\"), filePath.lastIndexOf("/"));
  if (idx <= 0) {
    return null;
  }

  return filePath.slice(0, idx);
}

function extractFolderFromPath(pathValue: string): string | null {
  const value = (pathValue ?? "").trim();
  if (!value) {
    return null;
  }

  const normalized = value.replace(/\//g, "\\");
  const idx = normalized.lastIndexOf("\\");
  if (idx <= 0) {
    return null;
  }

  return normalized.slice(0, idx);
}

function extractDatabaseNameFromSource(source: string): string | null {
  const value = (source ?? "").trim();
  if (!value) {
    return null;
  }

  const parts = value.split(";").map((p) => p.trim()).filter(Boolean);
  for (const part of parts) {
    const eq = part.indexOf("=");
    if (eq <= 0) {
      continue;
    }

    const key = part.slice(0, eq).trim().toLowerCase();
    const val = part.slice(eq + 1).trim();
    if (!val) {
      continue;
    }

    if (key === "database" || key === "initial catalog") {
      return val;
    }
  }

  return null;
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

type MsSqlDraft = {
  server: string;
  database: string;
  auth: "sql" | "windows";
  user: string;
  password: string;
  trustServerCertificate: boolean;
};

const defaultMsSqlDraft: MsSqlDraft = {
  server: "",
  database: "",
  auth: "sql",
  user: "",
  password: "",
  trustServerCertificate: true,
};

export default function App() {
  const smartPageSize = 5;
  const [tab, setTab] = useState<"system" | "disks" | "backup" | "settings" | "about">("system");
  const [theme, setTheme] = useState<"light" | "dark">(() =>
    localStorage.getItem("aria-theme") === "dark" ? "dark" : "light"
  );
  const [status, setStatus] = useState("");
  const [error, setError] = useState<string | null>(null);

  const [disks, setDisks] = useState<DiskRow[]>([]);
  const [selectedDisk, setSelectedDisk] = useState<DiskRow | null>(null);
  const [smart, setSmart] = useState<SmartRow[]>([]);
  const [smartPage, setSmartPage] = useState(1);

  const [jobs, setJobs] = useState<BackupJob[]>([]);
  const [logs, setLogs] = useState<BackupLog[]>([]);
  const [logFilterStatus, setLogFilterStatus] = useState("");
  const [selectedJob, setSelectedJob] = useState<BackupJob | null>(null);
  const [selectedMsSql, setSelectedMsSql] = useState<MsSqlDraft | null>(null);
  const [runningJobIds, setRunningJobIds] = useState<Record<string, boolean>>({});
  const [backupTopTab, setBackupTopTab] = useState<"configure" | "active">("configure");
  const [backupPageTab, setBackupPageTab] = useState<"tasks" | "history" | "new">("tasks");
  const [svcLine, setSvcLine] = useState("");
  const [windowsService, setWindowsService] = useState<WindowsServiceStatus | null>(null);

  const [settings, setSettings] = useState<SettingsDto | null>(null);
  const [serviceVersion, setServiceVersion] = useState<string | null>(null);
  const [launchAtStartup, setLaunchAtStartup] = useState(true);
  const [serviceSettingsExpanded, setServiceSettingsExpanded] = useState(false);
  const [outboundSettingsExpanded, setOutboundSettingsExpanded] = useState(false);
  /** Сообщение хоста: тип запуска службы — Automatic (null = ещё не приходило). */
  const [autostartServiceBootAuto, setAutostartServiceBootAuto] = useState<boolean | null>(null);

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

  const [editUseAdvancedCron, setEditUseAdvancedCron] = useState(false);
  const [editAdvancedCron, setEditAdvancedCron] = useState("");
  const [editPreset, setEditPreset] = useState<"daily" | "weekly" | "monthly">("daily");
  const [editSchHour, setEditSchHour] = useState(2);
  const [editSchMinute, setEditSchMinute] = useState(0);
  const [editSchDow, setEditSchDow] = useState("MON");
  const [editSchDom, setEditSchDom] = useState(1);

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
  const [smartCustomCron, setSmartCustomCron] = useState("0 0 * * * ?");

  const [systemInfo, setSystemInfo] = useState<SystemInfoDto | null>(null);
  const [outboundScheduleMode, setOutboundScheduleMode] = useState<SmartScheduleMode>("interval");
  const [outboundIntervalMin, setOutboundIntervalMin] = useState(30);
  const [outboundHour, setOutboundHour] = useState(2);
  const [outboundMinute, setOutboundMinute] = useState(0);
  const [outboundCustomCron, setOutboundCustomCron] = useState("0 0/30 * * * ?");

  const parseConnectionString = useCallback((source: string): MsSqlDraft => {
    const draft: MsSqlDraft = { ...defaultMsSqlDraft };
    const parts = (source ?? "")
      .split(";")
      .map((p) => p.trim())
      .filter(Boolean);

    for (const part of parts) {
      const eq = part.indexOf("=");
      if (eq <= 0) continue;
      const key = part.slice(0, eq).trim().toLowerCase();
      const value = part.slice(eq + 1).trim();
      if (!value) continue;

      if (key === "data source" || key === "server") draft.server = value;
      else if (key === "initial catalog" || key === "database") draft.database = value;
      else if (key === "user id" || key === "uid" || key === "user") draft.user = value;
      else if (key === "password" || key === "pwd") draft.password = value;
      else if (key === "integrated security" || key === "trusted_connection") {
        const normalized = value.toLowerCase();
        if (normalized === "true" || normalized === "sspi" || normalized === "yes") {
          draft.auth = "windows";
        }
      } else if (key === "trust server certificate") {
        const normalized = value.toLowerCase();
        draft.trustServerCertificate = normalized === "true" || normalized === "yes";
      }
    }

    if (draft.auth === "sql" && !draft.user && !draft.password) {
      const maybeWindows = (source ?? "").toLowerCase();
      if (maybeWindows.includes("integrated security=true") || maybeWindows.includes("trusted_connection=true")) {
        draft.auth = "windows";
      }
    }

    return draft;
  }, []);

  const buildConnectionString = useCallback((draft: MsSqlDraft): string => {
    const chunks: string[] = [];
    if (draft.server.trim()) chunks.push(`Data Source=${draft.server.trim()}`);
    if (draft.database.trim()) chunks.push(`Initial Catalog=${draft.database.trim()}`);
    if (draft.auth === "windows") {
      chunks.push("Integrated Security=True");
    } else {
      if (draft.user.trim()) chunks.push(`User ID=${draft.user.trim()}`);
      if (draft.password.trim()) chunks.push(`Password=${draft.password}`);
    }
    chunks.push("Connect Timeout=20");
    chunks.push("Encrypt=True");
    chunks.push(`Trust Server Certificate=${draft.trustServerCertificate ? "True" : "False"}`);
    return chunks.join(";");
  }, []);

  const cronValue = useMemo(() => {
    if (useAdvancedCron && advancedCron.trim()) return advancedCron.trim();
    return buildCron(preset, schHour, schMinute, schDow, schDom);
  }, [useAdvancedCron, advancedCron, preset, schHour, schMinute, schDow, schDom]);

  const editCronValue = useMemo(() => {
    if (editUseAdvancedCron && editAdvancedCron.trim()) return editAdvancedCron.trim();
    return buildCron(editPreset, editSchHour, editSchMinute, editSchDow, editSchDom);
  }, [editUseAdvancedCron, editAdvancedCron, editPreset, editSchHour, editSchMinute, editSchDow, editSchDom]);

  const backupJobsDisplayed = useMemo(
    () =>
      backupTopTab === "active"
        ? jobs.filter((j) => j.isEnabled || Boolean(runningJobIds[j.id]))
        : jobs,
    [backupTopTab, jobs, runningJobIds]
  );

  const lastLogByJobId = useMemo(() => {
    const m = new Map<string, BackupLog>();
    for (const logRow of logs) {
      if (!m.has(logRow.jobId)) {
        m.set(logRow.jobId, logRow);
      }
    }
    return m;
  }, [logs]);

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
      setStatus(`Диски обновлены: ${d.length}`);
    } catch (e) {
      showErr(e);
    }
  }, []);

  const loadDisks = useCallback(async () => {
    setError(null);
    try {
      const d = await apiGet<DiskRow[]>("/disks");
      setDisks(d);
    } catch (e) {
      showErr(e);
    }
  }, []);

  const refreshJobs = useCallback(async (silent = false) => {
    setError(null);
    try {
      const j = await apiGet<BackupJob[]>("/backups");
      setJobs(j);
      if (!silent) {
        setStatus(`Список задач обновлён: ${j.length}`);
      }
    } catch (e) {
      showErr(e);
    }
  }, []);

  const refreshLogs = useCallback(async (silent = false) => {
    setError(null);
    try {
      const q =
        logFilterStatus ? `?status=${encodeURIComponent(logFilterStatus)}` : "";
      const l = await apiGet<BackupLog[]>(`/backups/logs${q}`);
      setLogs(l);
      if (!silent) {
        setStatus(
          logFilterStatus
            ? `Журнал обновлён: ${l.length} записей (фильтр: ${logFilterStatus})`
            : `Журнал обновлён: ${l.length} записей`
        );
      }
    } catch (e) {
      showErr(e);
    }
  }, [logFilterStatus]);

  const refreshSettings = useCallback(async () => {
    setError(null);
    try {
      const s = await apiGet<SettingsDto>("/settings");
      setSettings({
        ...s,
        apiBind: (s.apiBind ?? "all").toLowerCase() === "loopback" ? "loopback" : "all",
        apiSharedSecret: s.apiSharedSecret ?? "",
        outboundSyncEnabled: Boolean(s.outboundSyncEnabled),
        outboundSyncUrl: s.outboundSyncUrl ?? "",
        outboundSyncCron: s.outboundSyncCron ?? "0 0/30 * * * ?",
        melezhEnabled: s.melezhEnabled ?? true,
        melezhPort: s.melezhPort ?? 7788,
        melezhUiUrl: s.melezhUiUrl ?? "http://127.0.0.1:7788/ui",
        melezhServiceName: s.melezhServiceName ?? "AriaSignatureMelezhService",
        melezhServiceStatus: s.melezhServiceStatus ?? null,
        melezhRunning: Boolean(s.melezhRunning),
      });
    } catch (e) {
      showErr(e);
    }
  }, []);

  const refreshServiceVersion = useCallback(async () => {
    try {
      const s = await apiGet<ServiceStatusDto>("/status");
      setServiceVersion(s.version?.trim() || null);
    } catch {
      setServiceVersion(null);
    }
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  useEffect(() => {
    if (!selectedJob || selectedJob.type.toLowerCase() !== "mssql") {
      setSelectedMsSql(null);
      return;
    }
    setSelectedMsSql(parseConnectionString(selectedJob.source));
  }, [selectedJob, parseConnectionString]);

  useEffect(() => {
    if (!selectedJob) {
      return;
    }
    const parsed = parseBackupScheduleEditor(selectedJob.scheduleCron);
    if (parsed.kind === "simple") {
      setEditUseAdvancedCron(false);
      setEditPreset(parsed.preset);
      setEditSchHour(parsed.hour);
      setEditSchMinute(parsed.minute);
      setEditSchDow(parsed.dow);
      setEditSchDom(parsed.dom);
      setEditAdvancedCron(selectedJob.scheduleCron.trim());
    } else {
      setEditUseAdvancedCron(true);
      setEditAdvancedCron(selectedJob.scheduleCron.trim());
    }
  }, [selectedJob?.id, selectedJob?.scheduleCron]);

  useEffect(() => {
    if (tab !== "system") {
      return;
    }
    let cancelled = false;
    const load = async () => {
      setError(null);
      try {
        const s = await apiGet<SystemInfoDto>("/system");
        if (!cancelled) setSystemInfo(s);
      } catch (e) {
        if (!cancelled) {
          const pretty = toFriendlyError(e);
          setError(pretty.message);
        }
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [tab]);

  useEffect(() => {
    const initialLoad = async () => {
      try {
        await Promise.all([loadDisks(), refreshJobs(true), refreshSettings(), refreshServiceVersion()]);
        try {
          await refreshLogs(true);
        } catch (e) {
          showErr(e);
        }
      } finally {
        postToHost({ action: "appReady" });
      }
    };
    void initialLoad();
    postToHost({ action: "getAutostart" });
  }, [loadDisks, refreshJobs, refreshSettings, refreshServiceVersion]);

  useEffect(() => {
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
    const id = window.setInterval(ping, 12_000);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, []);

  useEffect(() => {
    if (tab !== "backup") {
      return;
    }
    void refreshJobs(true);
    void refreshLogs(true);
  }, [tab, refreshJobs, refreshLogs]);

  useEffect(() => {
    if (tab !== "backup" || backupPageTab !== "history") {
      return;
    }
    void refreshLogs(true);
  }, [tab, backupPageTab, logFilterStatus, refreshLogs]);

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
          if (typeof data.serviceBootAuto === "boolean") {
            setAutostartServiceBootAuto(data.serviceBootAuto);
          } else {
            setAutostartServiceBootAuto(null);
          }
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
    const serverCron = settings.smartMonitoringCron;
    const parsed = parseSmartCron(serverCron);
    setSmartScheduleMode(parsed.mode);
    setSmartIntervalMin(parsed.intervalMin);
    setSmartHour(parsed.hour);
    setSmartMinute(parsed.minute);
    setSmartCustomCron(serverCron);
  }, [settings?.smartMonitoringCron]);

  useEffect(() => {
    if (!settings?.outboundSyncCron) {
      return;
    }
    const serverCron = settings.outboundSyncCron;
    const parsed = parseSmartCron(serverCron);
    setOutboundScheduleMode(parsed.mode);
    setOutboundIntervalMin(parsed.intervalMin);
    setOutboundHour(parsed.hour);
    setOutboundMinute(parsed.minute);
    setOutboundCustomCron(serverCron);
  }, [settings?.outboundSyncCron]);

  const smartCronPreview = useMemo(
    () => buildSmartCron(smartScheduleMode, smartIntervalMin, smartHour, smartMinute, smartCustomCron),
    [smartScheduleMode, smartIntervalMin, smartHour, smartMinute, smartCustomCron]
  );

  const savedSmartCron = useMemo(() => (settings?.smartMonitoringCron ?? "").trim(), [settings?.smartMonitoringCron]);
  const smartCronMatchesSaved = savedSmartCron === smartCronPreview.trim();

  const outboundCronPreview = useMemo(
    () => buildSmartCron(outboundScheduleMode, outboundIntervalMin, outboundHour, outboundMinute, outboundCustomCron),
    [outboundScheduleMode, outboundIntervalMin, outboundHour, outboundMinute, outboundCustomCron]
  );
  const savedOutboundCron = useMemo(() => (settings?.outboundSyncCron ?? "").trim(), [settings?.outboundSyncCron]);
  const outboundCronMatchesSaved = savedOutboundCron === outboundCronPreview.trim();

  const totalSmartPages = useMemo(
    () => Math.max(1, Math.ceil(smart.length / smartPageSize)),
    [smart.length, smartPageSize]
  );

  const pagedSmart = useMemo(() => {
    const safePage = Math.min(Math.max(1, smartPage), totalSmartPages);
    const start = (safePage - 1) * smartPageSize;
    return smart.slice(start, start + smartPageSize);
  }, [smart, smartPage, totalSmartPages, smartPageSize]);

  useEffect(() => {
    if (smartPage > totalSmartPages) {
      setSmartPage(totalSmartPages);
    }
  }, [smartPage, totalSmartPages]);

  const loadSmart = async (disk: DiskRow) => {
    setSelectedDisk(disk);
    setSmartPage(1);
    setError(null);
    try {
      const m = await apiGet<SmartRow[]>(`/disks/${disk.id}/smart`);
      setSmart(m);
    } catch (e) {
      showErr(e);
    }
  };

  const clearSelectedSmartHistory = async () => {
    if (!selectedDisk) return;
    if (!confirm(`Очистить историю диска для «${selectedDisk.model}»?`)) return;
    setError(null);
    try {
      const result = await apiSend<ClearSmartResponse>(`/disks/${selectedDisk.id}/smart`, "DELETE");
      await loadSmart(selectedDisk);
      setSmartPage(1);
      setStatus(`История диска очищена: ${result.deleted ?? 0} записей.`);
    } catch (e) {
      showErr(e);
    }
  };

  const clearAllSmartHistory = async () => {
    if (
      !confirm(
        "Очистить всю историю дисков по всем устройствам? Это действие удалит накопленные записи и не может быть отменено."
      )
    ) {
      return;
    }
    setError(null);
    try {
      const result = await apiSend<ClearSmartResponse>("/disks/smart", "DELETE");
      if (selectedDisk) {
        await loadSmart(selectedDisk);
      }
      setSmartPage(1);
      setStatus(`Глобальная история дисков очищена: ${result.deleted ?? 0} записей.`);
    } catch (e) {
      showErr(e);
    }
  };

  const clearAllBackupLogs = async () => {
    if (!confirm("Очистить журнал задач архивации? Это действие удалит все записи журнала и не может быть отменено.")) {
      return;
    }
    setError(null);
    try {
      const result = await apiSend<ClearSmartResponse>("/backups/logs", "DELETE");
      await refreshLogs(true);
      setStatus(`Журнал задач очищен: ${result.deleted ?? 0} записей.`);
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

  const testSelectedMsSql = async () => {
    if (!selectedMsSql) return;
    setError(null);
    setStatus("Проверка MSSQL (редактирование)…");
    try {
      await apiSend("/backups/test-mssql", "POST", {
        server: selectedMsSql.server,
        database: selectedMsSql.database,
        auth: selectedMsSql.auth,
        user: selectedMsSql.user,
        password: selectedMsSql.password,
        trustServerCertificate: selectedMsSql.trustServerCertificate,
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
      await refreshJobs(true);
      setBackupPageTab("tasks");
    } catch (e) {
      showErr(e);
    }
  };

  const saveSelectedJob = async () => {
    if (!selectedJob) return;
    setError(null);
    if (editUseAdvancedCron && !editAdvancedCron.trim()) {
      setError("В расширенном режиме укажите непустое cron-выражение Quartz.");
      return;
    }
    try {
      const isMsSql = selectedJob.type.toLowerCase() === "mssql";
      const sourceForSave =
        isMsSql && selectedMsSql
          ? buildConnectionString(selectedMsSql)
          : selectedJob.source;
      const body: Record<string, unknown> = {
        name: selectedJob.name,
        type: selectedJob.type,
        source: sourceForSave,
        destination: selectedJob.destination,
        scheduleCron: editCronValue,
        retentionCount: selectedJob.retentionCount,
        isEnabled: selectedJob.isEnabled,
      };
      await apiSend(`/backups/${selectedJob.id}`, "PUT", body);
      setStatus("Задача сохранена.");
      setSelectedJob((prev) =>
        prev && prev.id === selectedJob.id ? { ...prev, scheduleCron: editCronValue } : prev
      );
      await refreshJobs(true);
    } catch (e) {
      showErr(e);
    }
  };

  const runJob = async (id: string) => {
    setError(null);
    setRunningJobIds((prev) => ({ ...prev, [id]: true }));
    try {
      const log = await apiSend<BackupLog>(`/backups/${id}/run`, "POST");
      await refreshLogs(true);
      await refreshJobs(true);
      const ok = (log.status ?? "").toLowerCase() === "succeeded";
      setStatus(
        ok
          ? "Архивация завершена успешно."
          : `Архивация завершена с ошибкой: ${log.message ?? "—"}`
      );
    } catch (e) {
      showErr(e);
    } finally {
      setRunningJobIds((prev) => {
        const next = { ...prev };
        delete next[id];
        return next;
      });
    }
  };

  const deleteJob = async (id: string) => {
    if (!confirm("Удалить задачу?")) return;
    setError(null);
    try {
      await fetch(`/api/v1/backups/${id}`, { method: "DELETE" });
      setStatus("Задача удалена.");
      setSelectedJob(null);
      await refreshJobs(true);
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
      await refreshJobs(true);
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
      const body: Record<string, unknown> = {
        apiPort: settings.apiPort,
        apiBind: settings.apiBind,
        apiSharedSecret: settings.apiSharedSecret,
        smartMonitoringCron: smartCronPreview,
        outboundSyncEnabled: settings.outboundSyncEnabled,
        outboundSyncUrl: settings.outboundSyncUrl,
        outboundSyncCron: outboundCronPreview,
      };
      await apiSend("/settings", "PUT", body);
      await refreshSettings();
      setStatus(
        "Настройки записаны. Расписание обновления дисков и исходящей синхронизации применено сразу. При смене порта перезапустите службу."
      );
    } catch (e) {
      showErr(e);
    }
  };

  const statusClass = (s: string) => {
    const x = s.toLowerCase();
    return x === "critical" ? "pill bad" : x === "warning" ? "pill warn" : "pill ok";
  };

  const renderTaskPathCell = (job: BackupJob) => {
    if (backupTopTab === "active") {
      const destination = (job.destination ?? "").trim();
      if (!destination) {
        return "—";
      }

      return (
        <div className="path-cell">
          <span className="path-text">{destination}</span>
          <button type="button" className="secondary" onClick={() => postToHost({ action: "openFolder", path: destination })}>
            Открыть папку
          </button>
        </div>
      );
    }

    if (job.type.toLowerCase() === "file") {
      const sourceFolder = extractFolderFromPath(job.source);
      if (!sourceFolder) {
        return "—";
      }

      return (
        <div className="path-cell">
          <span className="path-text">{sourceFolder}</span>
          <button type="button" className="secondary" onClick={() => postToHost({ action: "openFolder", path: sourceFolder })}>
            Открыть папку
          </button>
        </div>
      );
    }

    const dbName = extractDatabaseNameFromSource(job.source);
    return dbName ? (
      <div className="path-cell">
        <span className="path-text">{dbName}</span>
      </div>
    ) : (
      "—"
    );
  };

  return (
    <div className="app">
      <header className="header">
        <div className="header-brand">
          <img className="app-logo" src={logoSrc} width={72} height={72} alt="" />
          <div className="header-titles">
            <h1>AriaSignature</h1>
            <p className="subtitle">
              Диагностика дисков и резервное копирование. Локальная панель управления.
            </p>
          </div>
        </div>
        <nav className="tabs">
          <button className={tab === "system" ? "active" : ""} onClick={() => setTab("system")}>
            О системе
          </button>
          <button className={tab === "disks" ? "active" : ""} onClick={() => setTab("disks")}>
            Диски
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

      {tab === "system" && (
        <section className="panel">
          <div className="section-stack">
            <div className="toolbar">
              <button
                type="button"
                onClick={() => {
                  setError(null);
                  void (async () => {
                    try {
                      const s = await apiGet<SystemInfoDto>("/system");
                      setSystemInfo(s);
                      setStatus("Данные о системе обновлены.");
                    } catch (e) {
                      showErr(e);
                    }
                  })();
                }}
              >
                Обновить
              </button>
            </div>
            {!systemInfo && <p className="muted">Загрузка…</p>}
            {systemInfo && (
              <>
                <div className="system-info-section">
                  <h3>Компьютер и ОС</h3>
                  <table className="data info-table">
                    <thead>
                      <tr>
                        <th scope="col">Параметр</th>
                        <th scope="col">Значение</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr>
                        <td>Имя хоста</td>
                        <td>{systemInfo.hostName}</td>
                      </tr>
                      <tr>
                        <td>DNS host name</td>
                        <td>{systemInfo.dnsHostName?.trim() || "—"}</td>
                      </tr>
                      <tr>
                        <td>ОС</td>
                        <td>{systemInfo.osCaption?.trim() || "—"}</td>
                      </tr>
                      <tr>
                        <td>Версия ОС</td>
                        <td>{systemInfo.osVersion?.trim() || "—"}</td>
                      </tr>
                      <tr>
                        <td>Версия агента</td>
                        <td>{systemInfo.agentVersion}</td>
                      </tr>
                      <tr>
                        <td>Снимок (UTC)</td>
                        <td className="mono small">{systemInfo.collectedAtUtc}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
                <div className="system-info-section">
                  <h3>Процессор и память</h3>
                  <table className="data info-table">
                    <thead>
                      <tr>
                        <th scope="col">Параметр</th>
                        <th scope="col">Значение</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr>
                        <td>Процессор</td>
                        <td>{systemInfo.processorName?.trim() || "—"}</td>
                      </tr>
                      <tr>
                        <td>Логических процессоров</td>
                        <td>{systemInfo.logicalProcessors ?? "—"}</td>
                      </tr>
                      <tr>
                        <td>ОЗУ всего</td>
                        <td>
                          {systemInfo.totalRamBytes != null ? formatBytes(systemInfo.totalRamBytes) : "—"}
                        </td>
                      </tr>
                      <tr>
                        <td>ОЗУ доступно</td>
                        <td>
                          {systemInfo.availableRamBytes != null ? formatBytes(systemInfo.availableRamBytes) : "—"}
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
                <div className="system-info-section">
                  <h3>Видеокарта</h3>
                  {systemInfo.videoControllers.length === 0 ? (
                    <p className="muted">Данные недоступны.</p>
                  ) : (
                    <table className="data info-table">
                      <thead>
                        <tr>
                          <th>№</th>
                          <th>Устройство</th>
                        </tr>
                      </thead>
                      <tbody>
                        {systemInfo.videoControllers.map((name, i) => (
                          <tr key={i}>
                            <td className="mono">{i + 1}</td>
                            <td>{name}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </div>
                <div className="system-info-section">
                  <h3>Сетевые адреса</h3>
                  {systemInfo.networkAddresses.length === 0 ? (
                    <p className="muted">Активные интерфейсы не найдены.</p>
                  ) : (
                    <table className="data compact info-table">
                      <thead>
                        <tr>
                          <th>Интерфейс</th>
                          <th>Семейство</th>
                          <th>Адрес</th>
                        </tr>
                      </thead>
                      <tbody>
                        {systemInfo.networkAddresses.map((n, i) => (
                          <tr key={`${n.address}-${i}`}>
                            <td>{n.interfaceDescription?.trim() || "—"}</td>
                            <td>{n.family}</td>
                            <td className="mono">{n.address}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </div>
              </>
            )}
          </div>
        </section>
      )}

      {tab === "disks" && (
        <section className="panel">
          <div className="toolbar">
            <button type="button" onClick={() => void refreshDisks()}>
              Обновить данные
            </button>
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
              <h2>Карточка диска</h2>
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
                    <dd>{formatSsdLifePercent(selectedDisk)}</dd>
                    <dt>Наработка</dt>
                    <dd>
                      {formatPowerOnHours(selectedDisk.powerOnHours)}, включений {selectedDisk.powerCycleCount || "—"}
                    </dd>
                    <dt>Источник телеметрии</dt>
                    <dd>{formatTelemetrySource(selectedDisk)}</dd>
                    <dt>Диагностика</dt>
                    <dd>{selectedDisk.telemetryDegradationReason || "—"}</dd>
                  </dl>
                  <div className="row" style={{ justifyContent: "space-between", alignItems: "center" }}>
                    <h3>История дисков</h3>
                    <div className="row">
                      <button type="button" className="secondary" onClick={() => void clearSelectedSmartHistory()}>
                        Очистить историю диска
                      </button>
                      <button type="button" className="danger" onClick={() => void clearAllSmartHistory()}>
                        Очистить всю историю дисков
                      </button>
                    </div>
                  </div>
                  <table className="data compact">
                    <thead>
                      <tr>
                        <th>Время (UTC)</th>
                        <th>Темп.</th>
                        <th>Здоровье</th>
                      </tr>
                    </thead>
                    <tbody>
                      {pagedSmart.map((row, i) => (
                        <tr key={i}>
                          <td>{row.timestampUtc}</td>
                          <td>{formatTempC(row.temperatureCelsius)}</td>
                          <td>{formatHealthPercent(row.healthPercent)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {smart.length > smartPageSize && (
                    <div className="row" style={{ justifyContent: "flex-end", alignItems: "center", marginTop: 8, gap: 8 }}>
                      <button type="button" className="secondary" disabled={smartPage <= 1} onClick={() => setSmartPage((p) => Math.max(1, p - 1))}>
                        Назад
                      </button>
                      <span className="muted">
                        Страница {smartPage} из {totalSmartPages}
                      </span>
                      <button
                        type="button"
                        className="secondary"
                        disabled={smartPage >= totalSmartPages}
                        onClick={() => setSmartPage((p) => Math.min(totalSmartPages, p + 1))}
                      >
                        Вперед
                      </button>
                    </div>
                  )}
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
              Журнал
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
                    <th>
                      {backupTopTab === "configure" ? "Путь к базе / База данных" : "Папка архива"}
                    </th>
                    <th>Периодичность</th>
                    <th>Время</th>
                    <th>Последний запуск</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {backupTopTab === "active" && backupJobsDisplayed.length === 0 ? (
                    <tr>
                      <td colSpan={8} className="muted" style={{ padding: "16px 12px" }}>
                        Нет активных задач по фильтру этой вкладки: сюда входят задачи, включённые в расписание, и задача,
                        которая выполняется сейчас.
                      </td>
                    </tr>
                  ) : null}
                  {backupJobsDisplayed.map((j) => {
                    const { periodicity, time } = cronToScheduleParts(j.scheduleCron);
                    const last = lastLogByJobId.get(j.id);
                    const running = Boolean(runningJobIds[j.id]);
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
                        <td>{renderTaskPathCell(j)}</td>
                        <td>{periodicity}</td>
                        <td className="mono">{time}</td>
                        <td>
                          {running ? (
                            <span className="muted">Выполняется…</span>
                          ) : last ? (
                            <>
                              <span>{formatBackupLogStatus(last.status)}</span>
                              <span className="muted small" style={{ display: "block" }}>
                                {new Date(last.endTimeUtc ?? last.startTimeUtc).toLocaleString()}
                              </span>
                            </>
                          ) : (
                            "—"
                          )}
                        </td>
                        <td className="row-actions" onClick={(e) => e.stopPropagation()}>
                          <button type="button" className="secondary" onClick={() => setSelectedJob({ ...j })}>
                            Редактировать
                          </button>
                          <button type="button" disabled={running} onClick={() => void runJob(j.id)}>
                            {running ? "…" : "Запуск"}
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {selectedJob && (
              <div className="job-editor-details">
                <div className="editor-header">
                  <h3 style={{ margin: 0 }}>Редактирование: {selectedJob.name}</h3>
                  <button
                    type="button"
                    className="editor-close-btn"
                    aria-label="Закрыть редактор"
                    title="Закрыть редактор"
                    onClick={() => setSelectedJob(null)}
                  >
                    ×
                  </button>
                </div>
                <div className="job-editor-body box backup-edit-box">
                  <label>
                    Имя
                    <input
                      value={selectedJob.name}
                      onChange={(e) => setSelectedJob({ ...selectedJob, name: e.target.value })}
                    />
                  </label>
                  {selectedJob.type.toLowerCase() === "mssql" && selectedMsSql ? (
                    <div className="box">
                      <label>
                        Сервер
                        <input
                          value={selectedMsSql.server}
                          onChange={(e) => setSelectedMsSql({ ...selectedMsSql, server: e.target.value })}
                        />
                      </label>
                      <label>
                        База данных
                        <input
                          value={selectedMsSql.database}
                          onChange={(e) => setSelectedMsSql({ ...selectedMsSql, database: e.target.value })}
                        />
                      </label>
                      <label>
                        Аутентификация
                        <select
                          value={selectedMsSql.auth}
                          onChange={(e) => setSelectedMsSql({ ...selectedMsSql, auth: e.target.value as "sql" | "windows" })}
                        >
                          <option value="sql">SQL (логин / пароль)</option>
                          <option value="windows">Windows (Integrated)</option>
                        </select>
                      </label>
                      {selectedMsSql.auth === "sql" && (
                        <>
                          <label>
                            Логин
                            <input
                              autoComplete="off"
                              value={selectedMsSql.user}
                              onChange={(e) => setSelectedMsSql({ ...selectedMsSql, user: e.target.value })}
                            />
                          </label>
                          <label>
                            Пароль
                            <input
                              type="password"
                              autoComplete="off"
                              value={selectedMsSql.password}
                              onChange={(e) => setSelectedMsSql({ ...selectedMsSql, password: e.target.value })}
                            />
                          </label>
                        </>
                      )}
                      <button type="button" className="secondary" onClick={() => void testSelectedMsSql()}>
                        Проверить подключение
                      </button>
                    </div>
                  ) : (
                    <label>
                      Источник (путь или строка подключения)
                      <textarea
                        value={selectedJob.source}
                        onChange={(e) => setSelectedJob({ ...selectedJob, source: e.target.value })}
                        rows={3}
                      />
                    </label>
                  )}
                  <label>
                    {backupTopTab === "active" ? "Папка архива" : "Папка архивов"}
                    <input
                      value={selectedJob.destination}
                      onChange={(e) => setSelectedJob({ ...selectedJob, destination: e.target.value })}
                    />
                  </label>
                  <h4 style={{ margin: "12px 0 8px" }}>Расписание</h4>
                  <p className="hint">Время задаётся в локальном часовом поясе Windows на этом ПК.</p>
                  <label className="check">
                    <input
                      type="checkbox"
                      checked={editUseAdvancedCron}
                      onChange={(e) => {
                        const on = e.target.checked;
                        if (!on) {
                          const parsed = parseBackupScheduleEditor(editAdvancedCron.trim());
                          if (parsed.kind !== "simple") {
                            setStatus(
                              "Это расписание нельзя выразить простыми пресетами. Оставлен расширенный режим Quartz."
                            );
                            return;
                          }
                          setEditPreset(parsed.preset);
                          setEditSchHour(parsed.hour);
                          setEditSchMinute(parsed.minute);
                          setEditSchDow(parsed.dow);
                          setEditSchDom(parsed.dom);
                        } else {
                          setEditAdvancedCron(
                            buildCron(editPreset, editSchHour, editSchMinute, editSchDow, editSchDom)
                          );
                        }
                        setEditUseAdvancedCron(on);
                      }}
                    />
                    Расширенный режим (cron Quartz)
                  </label>
                  {!editUseAdvancedCron ? (
                    <>
                      <label>
                        Периодичность
                        <select
                          value={editPreset}
                          onChange={(e) => setEditPreset(e.target.value as typeof editPreset)}
                        >
                          <option value="daily">Ежедневно</option>
                          <option value="weekly">Еженедельно</option>
                          <option value="monthly">Ежемесячно</option>
                        </select>
                      </label>
                      <div className="row">
                        <label>
                          Час
                          <input
                            type="number"
                            min={0}
                            max={23}
                            value={editSchHour}
                            onChange={(e) => setEditSchHour(+e.target.value)}
                          />
                        </label>
                        <label>
                          Минута
                          <input
                            type="number"
                            min={0}
                            max={59}
                            value={editSchMinute}
                            onChange={(e) => setEditSchMinute(+e.target.value)}
                          />
                        </label>
                      </div>
                      {editPreset === "weekly" && (
                        <label>
                          День недели
                          <select value={editSchDow} onChange={(e) => setEditSchDow(e.target.value)}>
                            {quartzDays.map((d) => (
                              <option key={d.v} value={d.v}>
                                {d.label}
                              </option>
                            ))}
                          </select>
                        </label>
                      )}
                      {editPreset === "monthly" && (
                        <label>
                          Число месяца
                          <input
                            type="number"
                            min={1}
                            max={28}
                            value={editSchDom}
                            onChange={(e) => setEditSchDom(+e.target.value)}
                          />
                        </label>
                      )}
                    </>
                  ) : (
                    <label>
                      Cron (Quartz)
                      <input
                        value={editAdvancedCron}
                        onChange={(e) => setEditAdvancedCron(e.target.value)}
                        placeholder="0 0 2 * * ?"
                        className="mono"
                      />
                    </label>
                  )}
                  {editUseAdvancedCron && <p className="cron-hint">{advancedCronHelp}</p>}
                  <p className="muted mono small">Quartz: {editCronValue}</p>
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
                    <button type="button" disabled={Boolean(runningJobIds[selectedJob.id])} onClick={() => void runJob(selectedJob.id)}>
                      {runningJobIds[selectedJob.id] ? "Выполняется…" : "Запустить"}
                    </button>
                    <button type="button" className="danger" onClick={() => void deleteJob(selectedJob.id)}>
                      Удалить
                    </button>
                  </div>
                </div>
              </div>
            )}
          </div>
          )}

          {backupPageTab === "history" && (
          <div className="backup-split-bottom">
            <h3>Журнал задач</h3>
            <div className="toolbar">
              <select value={logFilterStatus} onChange={(e) => setLogFilterStatus(e.target.value)}>
                <option value="">Все статусы</option>
                <option value="Succeeded">Успех</option>
                <option value="Failed">Ошибка</option>
              </select>
              <button type="button" onClick={() => void refreshLogs()}>
                Обновить журнал
              </button>
              <button type="button" className="danger" onClick={() => void clearAllBackupLogs()}>
                Очистить журнал задач
              </button>
            </div>
            <div className="table-wrap">
              <table className="data compact">
                <thead>
                  <tr>
                    <th>Дата</th>
                    <th>Задача</th>
                    <th>Папка архива</th>
                    <th>Статус</th>
                    <th>Размер</th>
                    <th>Результат</th>
                  </tr>
                </thead>
                <tbody>
                  {logs.map((l) => {
                    const folderPath = extractFolderFromLogMessage(l.message);
                    return (
                      <tr key={l.id}>
                        <td className="mono small">{new Date(l.startTimeUtc).toLocaleString()}</td>
                        <td>{jobs.find((x) => x.id === l.jobId)?.name ?? l.jobId}</td>
                        <td>
                          {folderPath ? (
                            <div className="path-cell">
                              <span className="path-text">{folderPath}</span>
                              <button type="button" className="secondary" onClick={() => postToHost({ action: "openFolder", path: folderPath })}>
                                Открыть папку
                              </button>
                            </div>
                          ) : (
                            "—"
                          )}
                        </td>
                        <td>{formatBackupLogStatus(l.status)}</td>
                        <td>{l.fileSizeBytes != null ? formatBytes(l.fileSizeBytes) : "—"}</td>
                        <td className="msg">{l.message}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
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
                <p className="hint">Время задаётся в локальном часовом поясе Windows на этом ПК.</p>
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

        </section>
      )}

      {tab === "about" && (
        <section className="panel about-page">
          <div className="about-brand">
            <img className="about-logo" src={logoSrc} width={120} height={120} alt="" />
            <h2>AriaSignature</h2>
            <p className="hint">Локальная панель для мониторинга дисков и резервного копирования баз 1С.</p>
            <p>
              <a href={GITHUB_REPO_URL} target="_blank" rel="noreferrer">
                Исходный код на GitHub
              </a>
            </p>
            <p className="muted">
              Лицензия:{" "}
              <a href="https://github.com/erlkidd/ariasignature/blob/production/LICENSE" target="_blank" rel="noreferrer">
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

          <details
            className="settings-collapsible"
            open={serviceSettingsExpanded}
            onToggle={(e) => setServiceSettingsExpanded((e.currentTarget as HTMLDetailsElement).open)}
          >
            <summary className="settings-collapsible-summary">Параметры службы</summary>
            <div className="settings-collapsible-body">
            <p className="hint">{settings.note}</p>
            <label>
            Порт HTTP API
            <input
              type="number"
              value={settings.apiPort}
              onChange={(e) => setSettings({ ...settings, apiPort: Number(e.target.value) })}
            />
            </label>
            <p className="hint">
            Панель на этом ПК подключается к <span className="mono">127.0.0.1:{settings.apiPort}</span>. С другой машины в VPN/LAN используйте{" "}
            <span className="mono">http://&lt;IP_этого_ПК&gt;:{settings.apiPort}/api/v1/…</span> (см. <span className="mono">docs/API.md</span>).
            </p>
            <label>
            Привязка сокета API
            <select
              value={settings.apiBind}
              onChange={(e) => setSettings({ ...settings, apiBind: e.target.value as "all" | "loopback" })}
            >
              <option value="all">Все интерфейсы (доступ по IP / VPN)</option>
              <option value="loopback">Только localhost (без входящих из сети)</option>
            </select>
            </label>
            <label>
            Токен для удалённого API (опционально)
            <input
              type="text"
              autoComplete="off"
              value={settings.apiSharedSecret}
              onChange={(e) => setSettings({ ...settings, apiSharedSecret: e.target.value })}
              placeholder="Пусто — любой, кто достучится до порта, читает API"
            />
            </label>
            <div className="row">
            <button
              type="button"
              className="secondary"
              onClick={() =>
                setSettings((prev) =>
                  prev
                    ? {
                        ...prev,
                        apiSharedSecret:
                          typeof globalThis.crypto !== "undefined" && "randomUUID" in globalThis.crypto
                            ? globalThis.crypto.randomUUID()
                            : `${Date.now()}-${Math.random().toString(36).slice(2, 14)}`,
                      }
                    : prev
                )
              }
            >
              Сгенерировать токен
            </button>
            </div>
            <p className="hint">
            Если токен задан, запросы не с localhost должны передавать{" "}
            <span className="mono">Authorization: Bearer &lt;токен&gt;</span> или <span className="mono">X-Aria-Api-Key</span>.
            </p>
            <label>
            Обновление дисков
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
                  value={smartCustomCron}
                  onChange={(e) => setSmartCustomCron(e.target.value)}
                  placeholder="0 0/15 * * * ?"
                />
              </label>
              <p className="hint">
                Расширенный режим: вручную задаётся выражение Quartz (шесть полей через пробел). Это альтернатива пресетам;
                значение сохраняется в базу как расписание опроса дисков.
              </p>
              <p className="hint">
                Формат: <span className="mono">секунда минута час день_месяца месяц день_недели</span>. Пример каждые 15 минут:{" "}
                <span className="mono">0 0/15 * * * ?</span>. Для «каждый час» используйте пресет или{" "}
                <span className="mono">0 0 * * * ?</span> — не задавайте <span className="mono">*/60</span> в поле минуты (в Quartz оно неверно).
              </p>
              </>
            )}
            <p className="hint">
            В базе сохранено: <span className="mono">{savedSmartCron || "—"}</span>
            {!smartCronMatchesSaved && (
              <>
                {" "}
                · после сохранения будет: <span className="mono">{smartCronPreview.trim() || "—"}</span>
              </>
            )}
            </p>
            </div>
          </details>

          <details
            className="settings-collapsible"
            open={outboundSettingsExpanded}
            onToggle={(e) => setOutboundSettingsExpanded((e.currentTarget as HTMLDetailsElement).open)}
          >
            <summary className="settings-collapsible-summary">Исходящая синхронизация (POST)</summary>
            <div className="settings-collapsible-body">
            <p className="hint">
            Данные о системе, дисках и архивации отправляются на ваш сервер по расписанию.
            </p>
            <label className="check">
            <input
              type="checkbox"
              checked={settings.outboundSyncEnabled}
              onChange={(e) => setSettings({ ...settings, outboundSyncEnabled: e.target.checked })}
            />
            Включить периодическую отправку JSON на коллектор
            </label>
            <label>
            URL коллектора (http/https, полный адрес с путём при необходимости)
            <input
              value={settings.outboundSyncUrl}
              onChange={(e) => setSettings({ ...settings, outboundSyncUrl: e.target.value })}
              placeholder="https://collector.example.com:8443/api/v1/aria/ingest"
            />
            </label>
            <label>
            Расписание отправки
            <select
              value={outboundScheduleMode}
              onChange={(e) => setOutboundScheduleMode(e.target.value as SmartScheduleMode)}
            >
              <option value="interval">Каждые N минут</option>
              <option value="daily">Ежедневно в указанное время</option>
              <option value="custom">Расширенный режим (Quartz)</option>
            </select>
            </label>
            {outboundScheduleMode === "interval" && (
              <label>
              Интервал
              <select
                value={outboundIntervalMin}
                onChange={(e) => {
                  setOutboundIntervalMin(Number(e.target.value));
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
            {outboundScheduleMode === "daily" && (
              <div className="row">
              <label>
                Час
                <input
                  type="number"
                  min={0}
                  max={23}
                  value={outboundHour}
                  onChange={(e) => {
                    const h = Math.max(0, Math.min(23, Number(e.target.value) || 0));
                    setOutboundHour(h);
                  }}
                />
              </label>
              <label>
                Минута
                <input
                  type="number"
                  min={0}
                  max={59}
                  value={outboundMinute}
                  onChange={(e) => {
                    const m = Math.max(0, Math.min(59, Number(e.target.value) || 0));
                    setOutboundMinute(m);
                  }}
                />
              </label>
              </div>
            )}
            {outboundScheduleMode === "custom" && (
              <>
              <label>
                Cron Quartz
                <input
                  value={outboundCustomCron}
                  onChange={(e) => setOutboundCustomCron(e.target.value)}
                  placeholder="0 0/30 * * * ?"
                  className="mono"
                />
              </label>
              <p className="hint">
                Расширенный режим: вручную задаётся выражение Quartz (шесть полей через пробел). Значение сохраняется в
                базу как расписание исходящей отправки.
              </p>
              <p className="hint">
                Формат: <span className="mono">секунда минута час день_месяца месяц день_недели</span>. Пример каждые 30
                минут: <span className="mono">0 0/30 * * * ?</span>. Справка:{" "}
                <a
                  href="https://www.quartz-scheduler.org/documentation/quartz-2.3.0/tutorials/crontrigger.html"
                  target="_blank"
                  rel="noreferrer"
                >
                  Quartz CronTrigger
                </a>
                .
              </p>
              </>
            )}
            <p className="hint">
            В базе сохранено: <span className="mono">{savedOutboundCron || "—"}</span>
            {!outboundCronMatchesSaved && (
              <>
                {" "}
                · после сохранения будет: <span className="mono">{outboundCronPreview.trim() || "—"}</span>
              </>
            )}
            </p>
            </div>
          </details>

          <details className="settings-collapsible" open>
            <summary className="settings-collapsible-summary">Melezh / OpenIntegrations</summary>
            <div className="settings-collapsible-body">
              <p className="hint">
                HTTP-шлюз для интеграций OpenIntegrations (Telegram, HTTP, БД и др.). Сбор телеметрии AriaSignature для 1С
                выполняется напрямую через API на порту {settings.apiPort}, без Melezh.
              </p>
              <p>
                <strong>Служба:</strong>{" "}
                {settings.melezhServiceStatus === "Running" ? (
                  <span className="pill ok">запущена</span>
                ) : settings.melezhServiceStatus ? (
                  <span className="pill warn">{settings.melezhServiceStatus}</span>
                ) : (
                  <span className="muted">—</span>
                )}
                {settings.melezhRunning ? (
                  <>
                    {" "}
                    · <span className="pill ok">Web UI доступен</span>
                  </>
                ) : (
                  <>
                    {" "}
                    · <span className="pill bad">Web UI недоступен</span>
                  </>
                )}
              </p>
              <p className="hint">
                Порт: <span className="mono">{settings.melezhPort}</span> · служба{" "}
                <span className="mono">{settings.melezhServiceName}</span>
              </p>
              <div className="row">
                <a href={settings.melezhUiUrl} target="_blank" rel="noreferrer">
                  Открыть Web UI Melezh
                </a>
                <button type="button" className="secondary" onClick={() => void refreshSettings()}>
                  Обновить статус
                </button>
              </div>
            </div>
          </details>

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

          <h2>Автозапуск при входе в Windows</h2>
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
          <p className="hint">
            Флажок включает или выключает автозапуск панели для вашей учётной записи (настраивает приложение само, без
            ручного редактирования реестра). Фоновая служба при установке уже получает тип запуска «Автоматически»; из
            настроек запросов администратора не будет.
          </p>
          {launchAtStartup && autostartServiceBootAuto === false ? (
            <p className="hint warn">
              Служба AriaSignatureService не в режиме автозапуска (возможно, сбой установки или ручное изменение).
              Переустановите приложение от имени администратора или обратитесь к администратору ПК.
            </p>
          ) : null}
        </section>
      )}

      <div
        className={`status-bar-effector${svcLine.includes("нет ответа") ? " err" : ""}`}
        role="status"
        aria-live="polite"
      >
        {svcLine || "Состояние API: ожидание…"}
      </div>

      <footer className="footer">
        <span>AriaSignature v{serviceVersion ?? UI_BUILD_VERSION}</span>
        <a href="/swagger" target="_blank" rel="noreferrer">
          Swagger / OpenAPI
        </a>
      </footer>
    </div>
  );
}
