namespace CrashBytes.Mediator;

/// <summary>
/// Central dispatcher for sending requests to a single handler and publishing
/// notifications to many. Resolve from DI as a scoped (or transient) service.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Sends a request through the pipeline behaviors to the registered
    /// handler and returns its response.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">Token propagated to behaviors and handler.</param>
    /// <returns>The handler's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to every registered handler in registration
    /// order. Handlers are invoked sequentially; an exception from one
    /// handler propagates and short-circuits the rest.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">Token propagated to each handler.</param>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is null.</exception>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
