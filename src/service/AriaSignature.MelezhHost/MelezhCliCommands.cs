namespace AriaSignature.MelezhHost;

/// <summary>
/// OInt/Melezh 0.12+ CLI uses Russian method names (МетодПоиска), not English CreateProject/RunProject.
/// </summary>
public static class MelezhCliCommands
{
    public const string CreateProjectMethod = "СоздатьПроект";
    public const string RunProjectMethod = "ЗапуститьПроект";

    public static string BuildCreateProjectArgs(string projectPath) =>
        $"{CreateProjectMethod} --path {QuoteArg(projectPath)}";

    public static string BuildRunProjectArgs(string projectPath, int port) =>
        $"{RunProjectMethod} --port {port} --proj {QuoteArg(projectPath)}";

    public static string QuoteArg(string value) =>
        $"\"{value.Replace("\"", "\\\"")}\"";

    public static (string FileName, string Arguments, string WorkingDirectory) ResolveOscriptInvocation(
        string oscriptExePath,
        string melezhAppOsPath,
        string cliArguments)
    {
        var quotedApp = QuoteArg(melezhAppOsPath);
        return (
            oscriptExePath,
            $"{quotedApp} {cliArguments}",
            Path.GetDirectoryName(oscriptExePath) ?? AppContext.BaseDirectory);
    }
}
