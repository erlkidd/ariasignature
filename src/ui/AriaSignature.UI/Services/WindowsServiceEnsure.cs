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
            if (TryStartServiceProcess(serviceExePath, out var processError))
            {
                warningMessage =
                    "Фоновая работа идёт через локальный процесс AriaSignature (запись в журнал и API на этом ПК). " +
                    "Служба Windows с именем AriaSignatureService на этом компьютере не зарегистрирована — обычно так бывает, " +
                    "если установка выполнялась без прав администратора или файлы скопированы вручную. " +
                    "Чтобы после перезагрузки всё поднималось автоматически, переустановите приложение из установщика (он запросит права администратора и зарегистрирует службу).";
                return;
            }

            warningMessage =
                $"Не удалось запустить фоновой сервис. Ни служба, ни fallback-процесс не стартовали.\n{ex.Message}\n{processError}";
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

    private static bool TryStartServiceProcess(string serviceExePath, out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(serviceExePath) || !File.Exists(serviceExePath))
            {
                error = $"Файл сервиса не найден: {serviceExePath}";
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = serviceExePath,
                WorkingDirectory = Path.GetDirectoryName(serviceExePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            if (process is null)
            {
                error = "Process.Start вернул null";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
