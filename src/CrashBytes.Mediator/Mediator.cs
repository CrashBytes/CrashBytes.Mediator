using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace CrashBytes.Mediator;

/// <summary>
/// Default <see cref="IMediator"/> implementation. Resolves handlers and
/// pipeline behaviors from <see cref="IServiceProvider"/> and dispatches via
/// compiled delegates cached by request/notification type.
/// </summary>
/// <remarks>
/// The first call for a given request type pays a one-time reflection cost to
/// build a delegate; every subsequent call is a dictionary lookup plus a
/// virtual invocation. The cache lives on a static field because the shape of
/// the dispatch (request type → handler interface) is process-global.
/// </remarks>
public class Mediator : IMediator
{
    private static readonly ConcurrentDictionary<Type, RequestDispatcher> RequestDispatchers = new();
    private static readonly ConcurrentDictionary<Type, NotificationDispatcher> NotificationDispatchers = new();

    private readonly IServiceProvider _serviceProvider;

    /// <summary>Creates a mediator backed by the given service provider.</summary>
    /// <param name="serviceProvider">Provider used to resolve handlers and behaviors.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is null.</exception>
    public Mediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var requestType = request.GetType();
        var dispatcher = RequestDispatchers.GetOrAdd(requestType, t => RequestDispatcher.Build(t, typeof(TResponse)));
        return dispatcher.InvokeAsync<TResponse>(_serviceProvider, request, cancellationToken);
    }

    /// <inheritdoc />
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification is null) throw new ArgumentNullException(nameof(notification));

        var notificationType = notification.GetType();
        var dispatcher = NotificationDispatchers.GetOrAdd(notificationType, NotificationDispatcher.Build);
        return dispatcher.InvokeAsync(_serviceProvider, notification, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Per-request-type dispatcher: caches handler/behavior service types
    //  and a compiled invoker that calls Handle without per-call reflection.
    // ──────────────────────────────────────────────────────────────────

    private sealed class RequestDispatcher
    {
        private readonly Type _handlerServiceType;
        private readonly Type _behaviorServiceType;
        private readonly Type _behaviorEnumerableType;
        private readonly HandlerInvoker _handlerInvoker;
        private readonly BehaviorInvoker _behaviorInvoker;

        private RequestDispatcher(
            Type handlerServiceType,
            Type behaviorServiceType,
            Type behaviorEnumerableType,
            HandlerInvoker handlerInvoker,
            BehaviorInvoker behaviorInvoker)
        {
            _handlerServiceType = handlerServiceType;
            _behaviorServiceType = behaviorServiceType;
            _behaviorEnumerableType = behaviorEnumerableType;
            _handlerInvoker = handlerInvoker;
            _behaviorInvoker = behaviorInvoker;
        }

        // Compiled delegate signature matches IRequestHandler<TRequest,TResponse>.Handle
        // but typed as object/object so we can cache one delegate per request type.
        private delegate object HandlerInvoker(object handler, object request, CancellationToken ct);

        // Compiled delegate matching IPipelineBehavior<TRequest,TResponse>.Handle.
        // The 'next' is boxed as Delegate so we can cache without TResponse.
        private delegate object BehaviorInvoker(object behavior, object request, Delegate next, CancellationToken ct);

        public static RequestDispatcher Build(Type requestType, Type responseType)
        {
            var handlerServiceType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);
            var behaviorServiceType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, responseType);
            var behaviorEnumerableType = typeof(IEnumerable<>).MakeGenericType(behaviorServiceType);

            return new RequestDispatcher(
                handlerServiceType,
                behaviorServiceType,
                behaviorEnumerableType,
                BuildHandlerInvoker(handlerServiceType, requestType),
                BuildBehaviorInvoker(behaviorServiceType, requestType, responseType));
        }

        private static HandlerInvoker BuildHandlerInvoker(Type handlerServiceType, Type requestType)
        {
            // (object handler, object request, CancellationToken ct) =>
            //     ((IRequestHandler<TRequest,TResponse>)handler).Handle((TRequest)request, ct)
            var handlerParam = Expression.Parameter(typeof(object), "handler");
            var requestParam = Expression.Parameter(typeof(object), "request");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

            var handleMethod = handlerServiceType.GetMethod("Handle")
                ?? throw new InvalidOperationException($"Handle method not found on {handlerServiceType}.");

            var call = Expression.Call(
                Expression.Convert(handlerParam, handlerServiceType),
                handleMethod,
                Expression.Convert(requestParam, requestType),
                ctParam);

            // Box the Task<TResponse> as object — we'll cast on the consumer side.
            var body = Expression.Convert(call, typeof(object));
            return Expression.Lambda<HandlerInvoker>(body, handlerParam, requestParam, ctParam).Compile();
        }

        private static BehaviorInvoker BuildBehaviorInvoker(Type behaviorServiceType, Type requestType, Type responseType)
        {
            // (object behavior, object request, Delegate next, CancellationToken ct) =>
            //     ((IPipelineBehavior<TRequest,TResponse>)behavior).Handle(
            //         (TRequest)request,
            //         (RequestHandlerDelegate<TResponse>)next,
            //         ct)
            var behaviorParam = Expression.Parameter(typeof(object), "behavior");
            var requestParam = Expression.Parameter(typeof(object), "request");
            var nextParam = Expression.Parameter(typeof(Delegate), "next");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

            var nextDelegateType = typeof(RequestHandlerDelegate<>).MakeGenericType(responseType);
            var handleMethod = behaviorServiceType.GetMethod("Handle")
                ?? throw new InvalidOperationException($"Handle method not found on {behaviorServiceType}.");

            var call = Expression.Call(
                Expression.Convert(behaviorParam, behaviorServiceType),
                handleMethod,
                Expression.Convert(requestParam, requestType),
                Expression.Convert(nextParam, nextDelegateType),
                ctParam);

            var body = Expression.Convert(call, typeof(object));
            return Expression.Lambda<BehaviorInvoker>(body, behaviorParam, requestParam, nextParam, ctParam).Compile();
        }

        public Task<TResponse> InvokeAsync<TResponse>(IServiceProvider serviceProvider, object request, CancellationToken cancellationToken)
        {
            var handler = serviceProvider.GetService(_handlerServiceType)
                ?? throw new InvalidOperationException(
                    $"No handler registered for request type '{request.GetType().FullName}'. " +
                    $"Expected a service implementing '{_handlerServiceType}'.");

            // Innermost stage — call the handler.
            RequestHandlerDelegate<TResponse> pipeline = () =>
                (Task<TResponse>)_handlerInvoker(handler, request, cancellationToken);

            // Wrap with behaviors in reverse so the first registered runs outermost.
            var behaviors = ResolveBehaviors(serviceProvider);
            for (var i = behaviors.Count - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var next = pipeline;
                pipeline = () => (Task<TResponse>)_behaviorInvoker(behavior, request, next, cancellationToken);
            }

            return pipeline();
        }

        private IReadOnlyList<object> ResolveBehaviors(IServiceProvider serviceProvider)
        {
            var raw = serviceProvider.GetService(_behaviorEnumerableType);
            if (raw is null) return Array.Empty<object>();

            var list = new List<object>();
            foreach (var b in (System.Collections.IEnumerable)raw) list.Add(b!);
            return list;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Per-notification-type dispatcher.
    // ──────────────────────────────────────────────────────────────────

    private sealed class NotificationDispatcher
    {
        private readonly Type _handlerEnumerableType;
        private readonly NotificationInvoker _invoker;

        private NotificationDispatcher(Type handlerEnumerableType, NotificationInvoker invoker)
        {
            _handlerEnumerableType = handlerEnumerableType;
            _invoker = invoker;
        }

        private delegate Task NotificationInvoker(object handler, object notification, CancellationToken ct);

        public static NotificationDispatcher Build(Type notificationType)
        {
            var handlerServiceType = typeof(INotificationHandler<>).MakeGenericType(notificationType);
            var handlerEnumerableType = typeof(IEnumerable<>).MakeGenericType(handlerServiceType);

            var handleMethod = handlerServiceType.GetMethod("Handle")
                ?? throw new InvalidOperationException($"Handle method not found on {handlerServiceType}.");

            var handlerParam = Expression.Parameter(typeof(object), "handler");
            var notificationParam = Expression.Parameter(typeof(object), "notification");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

            var call = Expression.Call(
                Expression.Convert(handlerParam, handlerServiceType),
                handleMethod,
                Expression.Convert(notificationParam, notificationType),
                ctParam);

            var invoker = Expression.Lambda<NotificationInvoker>(call, handlerParam, notificationParam, ctParam).Compile();
            return new NotificationDispatcher(handlerEnumerableType, invoker);
        }

        public async Task InvokeAsync(IServiceProvider serviceProvider, object notification, CancellationToken cancellationToken)
        {
            var raw = serviceProvider.GetService(_handlerEnumerableType);
            if (raw is null) return;

            foreach (var handler in (System.Collections.IEnumerable)raw)
            {
                if (handler is null) continue;
                await _invoker(handler, notification, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
