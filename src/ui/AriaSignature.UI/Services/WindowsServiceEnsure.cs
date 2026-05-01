using System.ServiceProcess;

namespace AriaSignature.UI.Services;

/// <summary>
/// Гарантирует, что фоновая служба с API запущена (имя совпадает с установщиком Inno Setup).
/// </summary>
public static class WindowsServiceEnsure
{
    public const string ServiceName = "AriaSignatureService";

    public static void TryStart(TimeSpan wait, out string? errorMessage)
    {
        errorMessage = null;
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
        }
        catch (InvalidOperationException ex)
        {
            errorMessage =
                $"Служба «{ServiceName}» не найдена или недоступна. Установите AriaSignature установщиком от имени администратора, чтобы зарегистрировать службу.\n{ex.Message}";
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            errorMessage = "Превышено время ожидания запуска службы. Проверьте оснастку «Службы» (services.msc).";
        }
        catch (Exception ex)
        {
            errorMessage = $"Не удалось запустить службу (нужны права на запуск службы): {ex.Message}";
        }
    }
}
