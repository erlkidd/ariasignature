namespace AriaSignature.Application.Abstractions;

/// <summary>Применяет расписание фонового обновления телеметрии дисков (Quartz) без перезапуска службы.</summary>
public interface ISmartRefreshCronApplier
{
    Task ApplyCronAsync(string cronExpression, CancellationToken cancellationToken);
}
