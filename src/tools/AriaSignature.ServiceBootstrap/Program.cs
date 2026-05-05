using System.Text;
using AriaSignature.WinSvc;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var serviceExePath = args.Length > 0 ? args[0].Trim().Trim('"') : string.Empty;
if (string.IsNullOrWhiteSpace(serviceExePath))
{
    Console.Error.WriteLine("Usage: AriaSignature.ServiceBootstrap.exe \"path\\to\\AriaSignature.Service.exe\"");
    return 2;
}

var logsRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "AriaSignature",
    "logs");
Directory.CreateDirectory(logsRoot);

var logPath = Path.Combine(logsRoot, $"bootstrap-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");

try
{
    using var logWriter = new StreamWriter(logPath, append: false, Encoding.UTF8);
    logWriter.WriteLine($"{DateTime.UtcNow:O} ServiceBootstrap starting.");
    logWriter.WriteLine($"serviceExePath={serviceExePath}");
    logWriter.WriteLine("marker=scm-bootstrap-start");

    var result = WindowsServiceInstaller.TryInstallAndStart(serviceExePath, logWriter.WriteLine);
    logWriter.WriteLine($"{DateTime.UtcNow:O} marker=scm-bootstrap-done success={result.Success} category={result.Category} exit={result.LastNonZeroExitCode}");
    logWriter.Flush();

    if (!result.Success)
    {
        Console.Error.WriteLine(WindowsServiceInstaller.BuildUserHint(result));
        return 1;
    }

    return 0;
}
catch (Exception ex)
{
    try
    {
        File.AppendAllText(logPath, $"{DateTime.UtcNow:O} Fatal: {ex}\n", Encoding.UTF8);
    }
    catch
    {
        // ignore
    }

    Console.Error.WriteLine(ex.Message);
    return 1;
}
