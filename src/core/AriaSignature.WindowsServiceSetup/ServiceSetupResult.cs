namespace AriaSignature.WinSvc;

public sealed record ServiceSetupResult(
    bool Success,
    string? ErrorMessage,
    ServiceSetupFailureCategory Category,
    int? LastNonZeroExitCode);
