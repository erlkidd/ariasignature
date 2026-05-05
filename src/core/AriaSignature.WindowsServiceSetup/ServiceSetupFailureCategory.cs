namespace AriaSignature.WinSvc;

public enum ServiceSetupFailureCategory
{
    None,
    AccessDenied,
    MissingServiceBinary,
    NotFoundOrDeleted,
    ScCommandFailed,
    ServiceStartTimeout,
    ServiceCrashedOnStart,
    Unknown
}
