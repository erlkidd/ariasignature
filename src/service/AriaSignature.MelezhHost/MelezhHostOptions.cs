namespace AriaSignature.MelezhHost;

public sealed class MelezhHostOptions
{
    public const string DefaultServiceName = "AriaSignatureMelezhService";
    public const int DefaultPort = 7788;

    public string ServiceName { get; init; } = DefaultServiceName;
    public string MelezhExePath { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public int Port { get; init; } = DefaultPort;
    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(5);

    public static MelezhHostOptions FromEnvironment()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var installRoot = Path.GetFullPath(Path.Combine(baseDir, ".."));
        var melezhDir = Path.Combine(installRoot, "melezh");
        var melezhBat = Path.Combine(melezhDir, "bin", "melezh.bat");
        var melezhExe = Path.Combine(melezhDir, "melezh.exe");
        var melezhLauncher = File.Exists(melezhBat) ? melezhBat : melezhExe;
        var projectDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AriaSignature",
            "melezh");
        Directory.CreateDirectory(projectDir);

        var port = DefaultPort;
        var portRaw = Environment.GetEnvironmentVariable("ARIASIGNATURE_MELEZH_PORT");
        if (int.TryParse(portRaw, out var parsedPort) && parsedPort is > 0 and <= 65535)
        {
            port = parsedPort;
        }

        return new MelezhHostOptions
        {
            MelezhExePath = melezhLauncher,
            ProjectPath = Path.Combine(projectDir, "AriaSignature.melezh"),
            Port = port
        };
    }
}
