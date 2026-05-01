namespace AriaSignature.Application.Abstractions;

public interface IAppSettingsService
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}
