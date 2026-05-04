using Microsoft.Extensions.DependencyInjection;

namespace CrashBytes.Mediator.Tests;

// Distinct request/notification types used only in DI-scan tests so that the
// scanner has something deterministic to find.
public record GetGreeting(string Name) : IRequest<string>;

public class GetGreetingHandler : IRequestHandler<GetGreeting, string>
{
    public Task<string> Handle(GetGreeting request, CancellationToken ct = default)
        => Task.FromResult($"Hello, {request.Name}");
}

public record CounterIncremented(int Amount) : INotification;

public class CounterIncrementedAuditHandler : INotificationHandler<CounterIncremented>
{
    public static int Calls;
    public Task Handle(CounterIncremented n, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

public class CounterIncrementedMetricsHandler : INotificationHandler<CounterIncremented>
{
    public static int Calls;
    public Task Handle(CounterIncremented n, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddCrashBytesMediator_WithMarkerType_RegistersMediator()
    {
        var services = new ServiceCollection();
        services.AddCrashBytesMediator(typeof(ServiceCollectionExtensionsTests));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        Assert.IsType<Mediator>(mediator);

        var result = await mediator.Send(new GetGreeting("World"));
        Assert.Equal("Hello, World", result);
    }

    [Fact]
    public async Task AddCrashBytesMediator_DiscoversNotificationHandlers()
    {
        CounterIncrementedAuditHandler.Calls = 0;
        CounterIncrementedMetricsHandler.Calls = 0;

        var services = new ServiceCollection();
        services.AddCrashBytesMediator(typeof(ServiceCollectionExtensionsTests));
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Publish(new CounterIncremented(5));

        Assert.Equal(1, CounterIncrementedAuditHandler.Calls);
        Assert.Equal(1, CounterIncrementedMetricsHandler.Calls);
    }

    [Fact]
    public void AddCrashBytesMediator_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ServiceCollectionExtensions.AddCrashBytesMediator(
                null!, new[] { typeof(ServiceCollectionExtensionsTests) }));
    }

    [Fact]
    public void AddCrashBytesMediator_NoMarkerTypes_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            services.AddCrashBytesMediator(Array.Empty<Type>()));
    }
}
