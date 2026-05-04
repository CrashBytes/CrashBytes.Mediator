namespace CrashBytes.Mediator;

/// <summary>
/// Marker interface for a fire-and-forget notification dispatched to zero or
/// more handlers via <see cref="IMediator.Publish{TNotification}"/>.
/// </summary>
public interface INotification { }

/// <summary>
/// Handles a notification of type <typeparamref name="TNotification"/>.
/// Multiple handlers may be registered for the same notification; all are
/// invoked sequentially.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    /// <summary>Handles the notification.</summary>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">Token observed by the handler.</param>
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}
