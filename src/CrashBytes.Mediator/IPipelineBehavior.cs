namespace CrashBytes.Mediator;

/// <summary>
/// Delegate representing the next stage in the request pipeline. Calling it
/// invokes the next behavior, or the request handler if this is the innermost
/// behavior.
/// </summary>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Pipeline behavior that wraps request handling. Behaviors compose around
/// the handler in registration order — the first registered behavior is the
/// outermost wrapper, and the implementation must call the <c>next</c> delegate
/// exactly once (or short-circuit by returning a synthetic response).
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Wraps the next stage of the pipeline.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="next">Delegate that invokes the next stage.</param>
    /// <param name="cancellationToken">Token observed by the behavior.</param>
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default);
}
