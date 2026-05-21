namespace AriaSignature.MelezhHost;

public sealed record MelezhBootstrapEnsureResult(
    bool Upgraded,
    int PreviousVersion,
    int CurrentVersion);
