namespace AriaSignature.Infrastructure.Persistence;

public interface ISqliteDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
