import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiError, apiGet, apiSend } from "./api";

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
  temperatureCelsius: number;
  healthPercent: number;
  powerOnHours: number;
  powerCycleCount: number;
  reallocatedSectors: number;
  pendingSectors: number;
  uncorrectableErrors: number;
  status: string;
  updatedAtUtc: string;
};

interface SmartRow {
  diskId: string;
  temperatureCelsius: number;
  healthPercent: number;
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

const quartzDays = [
  { v: "SUN", label: "Воскресенье" },
  { v: "MON", label: "Понедельник" },
  { v: "TUE", label: "Вторник" },
  { v: "WED", label: "Среда" },
  { v: "THU", label: "Четверг" },
  { v: "FRI", label: "Пятница" },
  { v: "SAT", label: "Суббота" },
];

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

function describeCron(
  preset: "daily" | "weekly" | "monthly",
  hour: number,
  minute: number,
  dayOfWeek: string,
  dayOfMonth: number
): string {
  const d = quartzDays.find((x) => x.v === dayOfWeek)?.label ?? dayOfWeek;
  if (preset === "daily") return `Каждый день в ${hour.toString().padStart(2, "0")}:${minute.toString().padStart(2, "0")}`;
  if (preset === "weekly")
    return `Раз в неделю (${d}) в ${hour.toString().padStart(2, "0")}:${minute.toString().padStart(2, "0")}`;
  return `Раз в месяц (${dayOfMonth}-е число) в ${hour.toString().padStart(2, "0")}:${minute.toString().padStart(2, "0")}`;
}

function postToHost(payload: unknown) {
  const w = window as unknown as { chrome?: { webview?: { postMessage: (m: string) => void } } };
  if (w.chrome?.webview) {
    w.chrome.webview.postMessage(JSON.stringify(payload));
  }
}

export default function App() {
  const [tab, setTab] = useState<"disks" | "backup" | "settings">("disks");
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

  const cronValue = useMemo(() => {
    if (useAdvancedCron && advancedCron.trim()) return advancedCron.trim();
    return buildCron(preset, schHour, schMinute, schDow, schDom);
  }, [useAdvancedCron, advancedCron, preset, schHour, schMinute, schDow, schDom]);

  const cronHint = useMemo(() => {
    if (useAdvancedCron) return "Расширенный режим: выражение Quartz (сек мин час …)";
    return describeCron(preset, schHour, schMinute, schDow, schDom);
  }, [useAdvancedCron, preset, schHour, schMinute, schDow, schDom]);

  const applyTheme = useCallback((t: "light" | "dark") => {
    document.documentElement.dataset.theme = t;
    localStorage.setItem("aria-theme", t);
    setTheme(t);
  }, []);

  const showErr = (e: unknown) => {
    const msg = e instanceof ApiError ? e.body ?? e.message : e instanceof Error ? e.message : String(e);
    setError(msg);
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
    void refreshDisks();
    void refreshJobs();
    void refreshLogs();
    void refreshSettings();
    postToHost({ action: "getAutostart" });
  }, [refreshDisks, refreshJobs, refreshLogs, refreshSettings]);

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
      } catch {
        /* ignore */
      }
    };
    chromeWebview.addEventListener("message", fn);
    return () => chromeWebview.removeEventListener("message", fn);
  }, []);

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
        <h1>AriaSignature</h1>
        <p className="subtitle">
          Диагностика накопителей и архивация 1С. Локальная панель управления; HTTP API для внешних систем (например 1С).
        </p>
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

      {status && !error && <div className="banner ok">{status}</div>}

      {tab === "disks" && (
        <section className="panel">
          <div className="toolbar">
            <button type="button" onClick={() => void refreshDisks()}>
              Обновить данные
            </button>
            <span className="hint">
              Данные собирает служба Windows (WMI/SMART). SQLite хранит историю и задания, не вашу базу 1С.
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
                      <td>{d.healthPercent}%</td>
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
                    <dd>{selectedDisk.temperatureCelsius} °C</dd>
                    <dt>Ресурс SSD</dt>
                    <dd>{selectedDisk.ssdLifeRemainingPercent ?? "н/д"}%</dd>
                    <dt>Наработка</dt>
                    <dd>
                      {selectedDisk.powerOnHours} ч, включений {selectedDisk.powerCycleCount}
                    </dd>
                    <dt>SMART</dt>
                    <dd>
                      Reallocated {selectedDisk.reallocatedSectors}, Pending {selectedDisk.pendingSectors}, Uncorrectable{" "}
                      {selectedDisk.uncorrectableErrors}
                    </dd>
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
                          <td>{row.temperatureCelsius}</td>
                          <td>{row.healthPercent}%</td>
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
        <section className="panel">
          <div className="grid2">
            <div>
              <h2>Новая задача</h2>
              <label>
                Имя
                <input value={jobName} onChange={(e) => setJobName(e.target.value)} placeholder="Ночной бэкап" />
              </label>
              <label>
                Тип
                <select value={jobType} onChange={(e) => setJobType(e.target.value as "file" | "msSql")}>
                  <option value="file">Файловая база (.1CD)</option>
                  <option value="msSql">Microsoft SQL Server</option>
                </select>
              </label>
              {jobType === "file" ? (
                <label>
                  Путь к файлу .1CD
                  <div className="row" style={{ alignItems: "stretch" }}>
                    <input
                      style={{ flex: 1 }}
                      value={fileSource}
                      onChange={(e) => setFileSource(e.target.value)}
                      placeholder="D:\Base\1Cv8.1CD"
                    />
                    <button type="button" className="secondary" onClick={() => postToHost({ action: "pickFile" })}>
                      Обзор…
                    </button>
                  </div>
                </label>
              ) : (
                <div className="box">
                  <p className="hint">
                    <strong>MSSQL:</strong> при режиме «Windows» резервная копия выполняется от имени{" "}
                    <strong>учётной записи службы</strong> Windows (services.msc → AriaSignatureService). У этой учётки
                    должны быть права на базу. При «SQL» учётные данные сохраняются в локальной SQLite приложения
                    в составе строки подключения задачи (ограничьте доступ к каталогу установки).
                  </p>
                  <label>
                    Сервер
                    <input value={msServer} onChange={(e) => setMsServer(e.target.value)} />
                  </label>
                  <label>
                    База данных
                    <input value={msDb} onChange={(e) => setMsDb(e.target.value)} />
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
                        <input value={msUser} onChange={(e) => setMsUser(e.target.value)} autoComplete="off" />
                      </label>
                      <label>
                        Пароль
                        <input
                          type="password"
                          value={msPassword}
                          onChange={(e) => setMsPassword(e.target.value)}
                          autoComplete="off"
                        />
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
                    style={{ flex: 1 }}
                    value={destFolder}
                    onChange={(e) => setDestFolder(e.target.value)}
                    placeholder="D:\Backups\1C"
                  />
                  <button type="button" className="secondary" onClick={() => postToHost({ action: "pickFolder" })}>
                    Папка…
                  </button>
                </div>
              </label>
              <label>
                Хранить копий (ротация)
                <input
                  type="number"
                  min={1}
                  value={retention}
                  onChange={(e) => setRetention(Number(e.target.value))}
                />
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
                    <select
                      value={preset}
                      onChange={(e) => setPreset(e.target.value as typeof preset)}
                    >
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
              <p className="cron-hint">
                <strong>Как это читать:</strong> {cronHint}
              </p>
              <p className="muted mono small">Quartz: {cronValue}</p>
              <button type="button" onClick={() => void createJob()}>
                Создать задачу
              </button>
            </div>
            <div>
              <h2>Задачи</h2>
              <table className="data">
                <thead>
                  <tr>
                    <th>Имя</th>
                    <th>Тип</th>
                    <th>Вкл</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {jobs.map((j) => (
                    <tr key={j.id} className={selectedJob?.id === j.id ? "sel" : ""}>
                      <td>{j.name}</td>
                      <td>{j.type}</td>
                      <td>{j.isEnabled ? "да" : "нет"}</td>
                      <td>
                        <button type="button" onClick={() => setSelectedJob({ ...j })}>
                          Изменить
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {selectedJob && (
                <div className="box">
                  <h3>Редактирование</h3>
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
                      onChange={(e) =>
                        setSelectedJob({ ...selectedJob, retentionCount: Number(e.target.value) })
                      }
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
              )}
              <h2>Журнал</h2>
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
              <table className="data compact">
                <thead>
                  <tr>
                    <th>Начало</th>
                    <th>Статус</th>
                    <th>Размер</th>
                    <th>Сообщение</th>
                  </tr>
                </thead>
                <tbody>
                  {logs.map((l) => (
                    <tr key={l.id}>
                      <td>{l.startTimeUtc}</td>
                      <td>{l.status}</td>
                      <td>{l.fileSizeBytes != null ? formatBytes(l.fileSizeBytes) : "—"}</td>
                      <td className="msg">{l.message}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </section>
      )}

      {tab === "settings" && settings && (
        <section className="panel">
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
            Cron обновления SMART (Quartz)
            <input
              value={settings.smartMonitoringCron}
              onChange={(e) => setSettings({ ...settings, smartMonitoringCron: e.target.value })}
            />
          </label>
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
        <span>AriaSignature v0.2</span>
        <a href="/swagger" target="_blank" rel="noreferrer">
          Swagger / OpenAPI
        </a>
      </footer>
    </div>
  );
}
