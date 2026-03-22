namespace CrashBytes.Mediator.Tests;

// ──────────────────────────────────────────────
//  Test fixtures
// ──────────────────────────────────────────────

public class Ping : IRequest<string> { public string Message { get; set; } = ""; }

public class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default)
        => Task.FromResult($"Pong: {request.Message}");
}

public class VoidCommand : IRequest { public bool Executed { get; set; } }

public class VoidCommandHandler : IRequestHandler<VoidCommand, Unit>
{
    public static bool WasExecuted { get; set; }
    public Task<Unit> Handle(VoidCommand request, CancellationToken cancellationToken = default)
    {
        WasExecuted = true;
        return Unit.Task;
    }
}

public class OrderPlaced : INotification { public int OrderId { get; set; } }

public class OrderPlacedHandler1 : INotificationHandler<OrderPlaced>
{
    public static int LastOrderId { get; set; }
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken = default)
    {
        LastOrderId = notification.OrderId;
        return Task.CompletedTask;
    }
}

public class OrderPlacedHandler2 : INotificationHandler<OrderPlaced>
{
    public static int LastOrderId { get; set; }
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken = default)
    {
        LastOrderId = notification.OrderId;
        return Task.CompletedTask;
    }
}

public class LoggingBehavior : IPipelineBehavior<Ping, string>
{
    public static bool WasCalled { get; set; }
    public async Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return await next();
    }
}

// ──────────────────────────────────────────────
//  Simple test service provider
// ──────────────────────────────────────────────

public class TestServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();
    private readonly Dictionary<Type, List<object>> _serviceCollections = new();

    public void Register<T>(T instance) where T : notnull
    {
        _services[typeof(T)] = instance;
    }

    public void Register(Type type, object instance)
    {
        _services[type] = instance;
    }

    public void RegisterCollection(Type elementType, IEnumerable<object> instances)
    {
        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var item in instances) list.Add(item);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
        _services[enumerableType] = list;
    }

    public object? GetService(Type serviceType)
    {
        _services.TryGetValue(serviceType, out var service);
        return service;
    }
}

// ──────────────────────────────────────────────
//  Tests
// ──────────────────────────────────────────────

public class UnitTests
{
    [Fact]
    public void Unit_Equals_AreEqual()
    {
        Assert.True(Unit.Value.Equals(default(Unit)));
        Assert.True(Unit.Value == default);
        Assert.False(Unit.Value != default);
    }

    [Fact]
    public void Unit_EqualsObject_UnitIsTrue()
    {
        Assert.True(Unit.Value.Equals((object)default(Unit)));
    }

    [Fact]
    public void Unit_EqualsObject_NonUnitIsFalse()
    {
        Assert.False(Unit.Value.Equals("not unit"));
    }

    [Fact]
    public void Unit_GetHashCode_IsZero()
    {
        Assert.Equal(0, Unit.Value.GetHashCode());
    }

    [Fact]
    public void Unit_ToString_ReturnsParens()
    {
        Assert.Equal("()", Unit.Value.ToString());
    }

    [Fact]
    public async Task Unit_Task_ReturnsCompletedTask()
    {
        var result = await Unit.Task;
        Assert.Equal(default, result);
    }
}

public class MediatorSendTests
{
    [Fact]
    public async Task Send_WithHandler_ReturnsResponse()
    {
        var sp = new TestServiceProvider();
        sp.Register(typeof(IRequestHandler<Ping, string>), new PingHandler());
        var mediator = new CrashBytes.Mediator.Mediator(sp);

        var result = await mediator.Send(new Ping { Message = "Hello" });
        Assert.Equal("Pong: Hello", result);
    }

    [Fact]
    public async Task Send_NoHandler_ThrowsInvalidOperationException()
    {
        var sp = new TestServiceProvider();
        var mediator = new CrashBytes.Mediator.Mediator(sp);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new Ping { Message = "Hello" }));
    }

    [Fact]
    public void Send_NullRequest_ThrowsArgumentNullException()
    {
        var sp = new TestServiceProvider();
        var mediator = new CrashBytes.Mediator.Mediator(sp);

        Assert.ThrowsAsync<ArgumentNullException>(() =>
            mediator.Send<string>(null!));
    }

    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CrashBytes.Mediator.Mediator(null!));
    }
}

public class MediatorPublishTests
{
    [Fact]
    public async Task Publish_NotifiesAllHandlers()
    {
        OrderPlacedHandler1.LastOrderId = 0;
        OrderPlacedHandler2.LastOrderId = 0;

        var sp = new TestServiceProvider();
        var handlerType = typeof(INotificationHandler<OrderPlaced>);
        sp.RegisterCollection(handlerType, new object[]
        {
            new OrderPlacedHandler1(),
            new OrderPlacedHandler2()
        });
        var mediator = new CrashBytes.Mediator.Mediator(sp);

        await mediator.Publish(new OrderPlaced { OrderId = 123 });

        Assert.Equal(123, OrderPlacedHandler1.LastOrderId);
        Assert.Equal(123, OrderPlacedHandler2.LastOrderId);
    }

    [Fact]
    public async Task Publish_NoHandlers_DoesNotThrow()
    {
        var sp = new TestServiceProvider();
        var mediator = new CrashBytes.Mediator.Mediator(sp);

        await mediator.Publish(new OrderPlaced { OrderId = 1 }); // should not throw
    }

    [Fact]
    public async Task Publish_NullNotification_ThrowsArgumentNullException()
    {
        var sp = new TestServiceProvider();
        var mediator = new CrashBytes.Mediator.Mediator(sp);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            mediator.Publish<OrderPlaced>(null!));
    }
}

public class MediatorPipelineTests
{
    [Fact]
    public async Task Send_WithPipelineBehavior_ExecutesBehavior()
    {
        LoggingBehavior.WasCalled = false;

        var sp = new TestServiceProvider();
        sp.Register(typeof(IRequestHandler<Ping, string>), new PingHandler());

        var behaviorType = typeof(IPipelineBehavior<Ping, string>);
        sp.RegisterCollection(behaviorType, new object[] { new LoggingBehavior() });

        var mediator = new CrashBytes.Mediator.Mediator(sp);
        var result = await mediator.Send(new Ping { Message = "test" });

        Assert.True(LoggingBehavior.WasCalled);
        Assert.Equal("Pong: test", result);
    }
}
