namespace AriaSignature.Infrastructure.Melezh;

internal static class MelezhHostLogDiagnostics
{
    private static readonly string[] CliFailureMarkers =
    [
        "CreateProject failed",
        "CreateProject --path",
        "code=99",
        "Неизвестный параметр",
        "Unknown parameter",
        "CommandLineArgumentParser.os"
    ];

    public static string? TryGetActionableErrorFromLatestLog()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AriaSignature",
                "logs");
            if (!Directory.Exists(logDir))
            {
                return null;
            }

            var latest = Directory
                .EnumerateFiles(logDir, "melezh-host-*.log", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest is null || latest.Length == 0)
            {
                return null;
            }

            var tail = ReadTailLines(latest.FullName, maxLines: 40);
            if (tail.Count == 0)
            {
                return null;
            }

            var combined = string.Join('\n', tail);
            if (!CliFailureMarkers.Any(m => combined.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            if (combined.Contains("CreateProject", StringComparison.OrdinalIgnoreCase))
            {
                return "Melezh host вызывает устаревшую CLI-команду CreateProject (нужны СоздатьПроект/ЗапуститьПроект). " +
                       "Обновите AriaSignature.MelezhHost и перезапустите службу AriaSignatureMelezhService.";
            }

            return "Ошибка CLI Melezh (код 99): проверьте melezh-host.log и bundle OInt (oscript, app.os).";
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadTailLines(string path, int maxLines)
    {
        var lines = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            lines.Add(line);
            if (lines.Count > maxLines)
            {
                lines.RemoveAt(0);
            }
        }

        return lines;
    }
}
