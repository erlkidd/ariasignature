namespace AriaSignature.Application.Telemetry;

/// <summary>Normalized telemetry inputs for composite disk health assessment.</summary>
public sealed record DiskHealthInput(
    int? SsdLifeRemainingPercent,
    bool SsdLifeEndOfLife,
    int? TemperatureCelsius,
    long PowerOnHours,
    int ReallocatedSectors,
    int PendingSectors,
    int UncorrectableErrors,
    bool PredictFailure,
    bool? SmartPassed,
    int? NvmeCriticalWarning,
    int? VendorHealthPercent,
    bool SmartCtlUsed,
    bool StorageReliabilityUsed,
    bool WmiUsed);
