namespace CrashBytes.Mediator;

/// <summary>
/// Marker interface for a request that returns <typeparamref name="TResponse"/>.
/// </summary>
public interface IRequest<TResponse> { }

/// <summary>
/// Marker interface for a request that returns no value.
/// </summary>
public interface IRequest : IRequest<Unit> { }

/// <summary>
/// Handles a request and returns a response.
/// </summary>
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker interface for a notification that can be sent to multiple handlers.
/// </summary>
public interface INotification { }

/// <summary>
/// Handles a notification.
/// </summary>
public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline behavior that wraps request handling for cross-cutting concerns.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default);
}

/// <summary>
/// Delegate representing the next action in the pipeline.
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Represents a void return type for requests that return no value.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>The single value of <see cref="Unit"/>.</summary>
    public static readonly Unit Value = default;

    /// <summary>Returns a completed task with <see cref="Unit.Value"/>.</summary>
    public static readonly Task<Unit> Task = System.Threading.Tasks.Task.FromResult(Value);

    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public override string ToString() => "()";
    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
}

/// <summary>
/// Central interface for sending requests and publishing notifications.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Sends a request to a single handler and returns the response.
    /// </summary>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}

/// <summary>
/// Default mediator implementation using <see cref="IServiceProvider"/> to resolve handlers.
/// </summary>
public class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    public Mediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

        var handler = _serviceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}.");

        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviors = (_serviceProvider.GetService(typeof(IEnumerable<>).MakeGenericType(behaviorType)) as System.Collections.IEnumerable)?
            .Cast<object>()
            .Reverse()
            .ToArray() ?? Array.Empty<object>();

        var handleMethod = handlerType.GetMethod("Handle")!;
        RequestHandlerDelegate<TResponse> pipeline = () =>
            (Task<TResponse>)handleMethod.Invoke(handler, new object[] { request, cancellationToken })!;

        foreach (var behavior in behaviors)
        {
            var currentPipeline = pipeline;
            var behaviorHandleMethod = behaviorType.GetMethod("Handle")!;
            pipeline = () =>
                (Task<TResponse>)behaviorHandleMethod.Invoke(behavior, new object[] { request, currentPipeline, cancellationToken })!;
        }

        return pipeline();
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification is null) throw new ArgumentNullException(nameof(notification));

        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notification.GetType());
        var handlersEnumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);
        var handlers = _serviceProvider.GetService(handlersEnumerableType) as System.Collections.IEnumerable;

        if (handlers is null) return;

        var handleMethod = handlerType.GetMethod("Handle")!;
        foreach (var handler in handlers)
        {
            await ((Task)handleMethod.Invoke(handler, new object[] { notification, cancellationToken })!).ConfigureAwait(false);
        }
    }
}
