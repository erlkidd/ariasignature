using System.Diagnostics;
using System.Text;

namespace AriaSignature.MelezhHost;

internal static class MelezhBootstrapCli
{
    public static async Task<int> RunAsync(
        MelezhHostOptions options,
        string cliArguments,
        CancellationToken cancellationToken)
    {
        var (fileName, arguments, workingDirectory) = MelezhCliCommands.ResolveOscriptInvocation(
            options.OscriptExePath,
            options.MelezhAppOsPath,
            cliArguments);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        var stdout = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
