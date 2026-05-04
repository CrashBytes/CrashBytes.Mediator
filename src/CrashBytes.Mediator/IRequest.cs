namespace CrashBytes.Mediator;

/// <summary>
/// Marker interface for a request that returns a response of type
/// <typeparamref name="TResponse"/>. Dispatched by <see cref="IMediator.Send{TResponse}"/>
/// to a single registered <see cref="IRequestHandler{TRequest, TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The type of value returned by the handler.</typeparam>
public interface IRequest<out TResponse> { }

/// <summary>
/// Marker interface for a request that returns no value. Implemented as a
/// request that returns <see cref="Unit"/>; the corresponding handler is
/// <c>IRequestHandler&lt;TRequest, Unit&gt;</c>.
/// </summary>
public interface IRequest : IRequest<Unit> { }

/// <summary>
/// Handles a request of type <typeparamref name="TRequest"/> and returns a
/// response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The request type, which must implement
/// <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The response type produced by the handler.</typeparam>
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request and returns the response.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="cancellationToken">Token observed by the handler.</param>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Convenience handler interface for requests that produce no response.
/// Implementations should return <see cref="Unit.Task"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{
}
