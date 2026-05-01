using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace AriaSignature.UI.Services;

/// <summary>
/// Гарантирует, что фоновая служба с API запущена (имя совпадает с установщиком Inno Setup).
/// </summary>
public static class WindowsServiceEnsure
{
    public const string ServiceName = "AriaSignatureService";

    public static void TryStartOrFallback(string serviceExePath, TimeSpan wait, out string? warningMessage)
    {
        warningMessage = null;
        try
        {
            KillOrphanServiceProcessesBestEffort();
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                return;
            }

            if (sc.Status == ServiceControllerStatus.StartPending)
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, wait);
                return;
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, wait);
            return;
        }
        catch (InvalidOperationException ex)
        {
            if (TryInstallAndStartWindowsService(serviceExePath, out var installError))
            {
                warningMessage = null;
                return;
            }

            warningMessage =
                $"Не удалось запустить фоновую службу.\n{ex.Message}\n{installError}";
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            warningMessage = "Превышено время ожидания запуска фонового сервиса. Проверьте оснастку «Службы» (services.msc).";
        }
        catch (Exception ex)
        {
            warningMessage = $"Не удалось запустить фоновый сервис (службу): {ex.Message}";
        }
    }

    private static void KillOrphanServiceProcessesBestEffort()
    {
        try
        {
            var activeServicePid = TryGetServiceProcessId();
            var currentPid = Process.GetCurrentProcess().Id;
            foreach (var process in Process.GetProcessesByName("AriaSignature.Service"))
            {
                try
                {
                    if (process.Id == currentPid)
                    {
                        continue;
                    }

                    if (activeServicePid.HasValue && process.Id == activeServicePid.Value)
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best-effort
                }
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static bool TryInstallAndStartWindowsService(string serviceExePath, out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(serviceExePath) || !File.Exists(serviceExePath))
            {
                error = $"Файл сервиса не найден: {serviceExePath}";
                return false;
            }

            var scPath = GetScExePath();
            if (string.IsNullOrEmpty(scPath))
            {
                error = "Не найден sc.exe";
                return false;
            }

            RunScBestEffort(scPath, $"stop {ServiceName}");
            RunScBestEffort(scPath, $"delete {ServiceName}");
            Thread.Sleep(1000);

            var createAttempts = new[]
            {
                $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto DisplayName= \"AriaSignature\" obj= LocalSystem",
                $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto DisplayName= \"AriaSignature Service\" obj= LocalSystem",
                $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto obj= LocalSystem"
            };

            var created = false;
            var lastCreateExit = -1;
            var lastCreateOutput = string.Empty;
            foreach (var createArgs in createAttempts)
            {
                if (RunSc(scPath, createArgs, out var createExit, out var createOutput))
                {
                    created = true;
                    break;
                }

                lastCreateExit = createExit;
                lastCreateOutput = createOutput;
                if (createExit == 1073)
                {
                    created = true;
                    break;
                }

                if (createExit != 1078)
                {
                    break;
                }
            }

            if (!created)
            {
                error = $"sc create failed ({lastCreateExit}): {lastCreateOutput}";
                return false;
            }

            RunScBestEffort(scPath, $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000");
            if (!RunSc(scPath, $"start {ServiceName}", out var startExit, out var startOutput) && startExit != 1056)
            {
                error = $"sc start failed ({startExit}): {startOutput}";
                return false;
            }

            using var sc = new ServiceController(ServiceName);
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? GetScExePath()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(winDir, "System32", "sc.exe"),
            Path.Combine(winDir, "Sysnative", "sc.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static void RunScBestEffort(string scPath, string args)
    {
        RunSc(scPath, args, out _, out _);
    }

    private static int? TryGetServiceProcessId()
    {
        try
        {
            var scPath = GetScExePath();
            if (string.IsNullOrEmpty(scPath))
            {
                return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = scPath,
                Arguments = "queryex " + ServiceName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var idx = line.IndexOf(':');
                if (idx < 0)
                {
                    continue;
                }

                var pidText = line[(idx + 1)..].Trim();
                if (int.TryParse(pidText, out var pid) && pid > 0)
                {
                    return pid;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static bool RunSc(string scPath, string args, out int exitCode, out string output)
    {
        output = string.Empty;
        var psi = new ProcessStartInfo
        {
            FileName = scPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        if (proc is null)
        {
            exitCode = -1;
            return false;
        }

        var stdOut = proc.StandardOutput.ReadToEnd();
        var stdErr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(20000);
        exitCode = proc.ExitCode;
        output = string.IsNullOrWhiteSpace(stdErr)
            ? stdOut
            : string.IsNullOrWhiteSpace(stdOut)
                ? stdErr
                : $"{stdOut}{Environment.NewLine}{stdErr}";
        return exitCode == 0;
    }
}
