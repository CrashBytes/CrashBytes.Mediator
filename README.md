# CrashBytes.Mediator

A small, free, **MIT-licensed** mediator for .NET. Implements the
request/response, notification, and pipeline-behavior patterns that
applications typically pull in `MediatR` for, with no licensing strings.

- **Targets:** `net8.0`, `net9.0`
- **Dependencies:** `Microsoft.Extensions.DependencyInjection.Abstractions`
- **License:** MIT

## Why this exists

`MediatR` moved to a commercial license in late 2025. Most teams only need a
few hundred lines of mediator plumbing — request dispatch, notification
broadcast, and a middleware pipeline. This package delivers exactly that, with
cached compiled-delegate dispatch so the per-call overhead is a dictionary
lookup plus a virtual call.

## Install

```bash
dotnet add package CrashBytes.Mediator
```

## Quick start

```csharp
using CrashBytes.Mediator;
using Microsoft.Extensions.DependencyInjection;

// 1. Define a request and its handler.
public record Ping(string Message) : IRequest<string>;

public class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken ct)
        => Task.FromResult($"Pong: {request.Message}");
}

// 2. Register and scan an assembly.
var services = new ServiceCollection();
services.AddCrashBytesMediator(typeof(Program));

// 3. Resolve and send.
var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();
var response = await mediator.Send(new Ping("hi"));   // "Pong: hi"
```

## Notifications (fire-and-forget broadcast)

```csharp
public record OrderPlaced(int OrderId) : INotification;

public class SendReceiptEmail : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced n, CancellationToken ct) { /* ... */ return Task.CompletedTask; }
}

public class UpdateInventory : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced n, CancellationToken ct) { /* ... */ return Task.CompletedTask; }
}

await mediator.Publish(new OrderPlaced(42));
```

All registered handlers run **sequentially** in registration order. An
exception from one handler propagates and short-circuits the rest.

## Pipeline behaviors (middleware)

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        _logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}

// Behaviors must be registered explicitly — order matters.
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

The first registered behavior is the **outermost** wrapper. With the registration
above, a `Send` call traces:

```
LoggingBehavior.Handle  →  ValidationBehavior.Handle  →  TheHandler.Handle
```

A behavior that does not call `next()` short-circuits the pipeline.

## Void requests

For commands that don't return a value, implement the parameterless `IRequest`:

```csharp
public record DeleteOrder(int Id) : IRequest;

public class DeleteOrderHandler : IRequestHandler<DeleteOrder, Unit>
{
    public Task<Unit> Handle(DeleteOrder request, CancellationToken ct)
    {
        // ... do work ...
        return Unit.Task;
    }
}

await mediator.Send(new DeleteOrder(1));
```

## Public API surface

| Type | Purpose |
| --- | --- |
| `IRequest<TResponse>` | Marker for requests with a response. |
| `IRequest` | Marker for requests with no response (returns `Unit`). |
| `IRequestHandler<TRequest, TResponse>` | Single handler per request type. |
| `IRequestHandler<TRequest>` | Convenience for void requests. |
| `INotification` | Marker for broadcasts. |
| `INotificationHandler<TNotification>` | Zero or more handlers per notification. |
| `IPipelineBehavior<TRequest, TResponse>` | Middleware around `Send`. |
| `RequestHandlerDelegate<TResponse>` | Pipeline `next` delegate. |
| `IMediator` / `Mediator` | Dispatcher interface and default implementation. |
| `Unit` | Void return type for `IRequest`. |
| `services.AddCrashBytesMediator(typeof(Marker))` | DI registration + handler scan. |

## Performance

The first dispatch for a given request type compiles a small expression tree
that calls the handler's `Handle` method directly. The compiled delegate is
cached on a static `ConcurrentDictionary` keyed by request type, so subsequent
calls cost a dictionary lookup plus a virtual invoke — no per-call reflection.

## Out of scope (for now)

- **Streaming requests** (`IStreamRequest<T>` / `IAsyncEnumerable<T>`) — planned for 1.2.
- **Notification publish strategies** (parallel, whenAll, etc.) — currently sequential foreach only.
- **Custom DI containers** — use `Microsoft.Extensions.DependencyInjection`. Any container that implements `IServiceProvider` and resolves `IEnumerable<T>` correctly will work, but only MEDI is tested.

## License

MIT. See [LICENSE](LICENSE).
