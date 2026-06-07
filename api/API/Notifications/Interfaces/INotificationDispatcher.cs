namespace API.Notifications.Interfaces;

/// <summary>
/// Owned abstraction for emitting a user-visible notification. Decouples notification fan-out from
/// the workers that produce events: download workers only persist ActionRecords; the periodic
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(string title, string body, CancellationToken ct);
}
