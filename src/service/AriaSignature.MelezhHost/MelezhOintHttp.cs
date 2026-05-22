namespace AriaSignature.MelezhHost;

/// <summary>OInt HTTP module tokens (requires http.json in oint-cli index — see prepare-melezh.ps1).</summary>
public static class MelezhOintHttp
{
    public const string Library = "http";
    public const string FuncGet = "Get";
    public const string FuncPostWithBody = "PostСТелом";
    public const string FuncPutWithBody = "PutСТелом";
    public const string FuncDeleteWithBody = "DeleteСТелом";
    public const string MethodGet = "GET";
    public const string MethodJson = "JSON";

    public static string FuncForAriaHttpMethod(string? ariaHttpMethod) =>
        ariaHttpMethod?.ToUpperInvariant() switch
        {
            "PUT" => FuncPutWithBody,
            "DELETE" => FuncDeleteWithBody,
            _ => FuncPostWithBody,
        };
}
