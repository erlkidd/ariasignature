using AriaSignature.Infrastructure.Persistence;

namespace AriaSignature.Api;

public sealed class ApiDatabaseInitializationHostedService : IHostedService
{
    private readonly ISqliteDatabaseInitializer _initializer;

    public ApiDatabaseInitializationHostedService(ISqliteDatabaseInitializer initializer)
    {
        _initializer = initializer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _initializer.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
