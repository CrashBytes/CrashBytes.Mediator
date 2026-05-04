using Microsoft.Extensions.DependencyInjection;

namespace CrashBytes.Mediator.Tests;

// Pipeline ordering & nesting fixtures: each behavior records "before" and
// "after" markers around its call to next() so we can assert the actual chain.
public static class PipelineLog
{
    public static readonly List<string> Entries = new();
    public static void Reset() => Entries.Clear();
}

public class OuterBehavior : IPipelineBehavior<Ping, string>
{
    public async Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken ct = default)
    {
        PipelineLog.Entries.Add("outer:before");
        var result = await next();
        PipelineLog.Entries.Add("outer:after");
        return result;
    }
}

public class InnerBehavior : IPipelineBehavior<Ping, string>
{
    public async Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken ct = default)
    {
        PipelineLog.Entries.Add("inner:before");
        var result = await next();
        PipelineLog.Entries.Add("inner:after");
        return result;
    }
}

public class ShortCircuitBehavior : IPipelineBehavior<Ping, string>
{
    public Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken ct = default)
    {
        PipelineLog.Entries.Add("short-circuit");
        return Task.FromResult("from-behavior");
    }
}

public class CancellationCapturingBehavior : IPipelineBehavior<Ping, string>
{
    public static CancellationToken? Captured;
    public Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken ct = default)
    {
        Captured = ct;
        return next();
    }
}

public class CancellationCapturingHandler : IRequestHandler<Ping, string>
{
    public static CancellationToken? Captured;
    public Task<string> Handle(Ping request, CancellationToken ct = default)
    {
        Captured = ct;
        return Task.FromResult("ok");
    }
}

public class PipelineNestingTests
{
    [Fact]
    public async Task TwoBehaviors_NestInRegistrationOrder()
    {
        PipelineLog.Reset();
        var services = new ServiceCollection();
        services.AddCrashBytesMediator(typeof(PipelineNestingTests));
        // First registered behavior must be outermost.
        services.AddTransient<IPipelineBehavior<Ping, string>, OuterBehavior>();
        services.AddTransient<IPipelineBehavior<Ping, string>, InnerBehavior>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new Ping { Message = "x" });

        Assert.Equal(
            new[] { "outer:before", "inner:before", "inner:after", "outer:after" },
            PipelineLog.Entries);
    }

    [Fact]
    public async Task Behavior_CanShortCircuit_HandlerNotInvoked()
    {
        PipelineLog.Reset();
        var sp = new TestServiceProvider();
        sp.Register(typeof(IRequestHandler<Ping, string>), new PingHandler());
        sp.RegisterCollection(typeof(IPipelineBehavior<Ping, string>),
            new object[] { new ShortCircuitBehavior() });

        var mediator = new Mediator(sp);
        var result = await mediator.Send(new Ping { Message = "ignored" });

        Assert.Equal("from-behavior", result);
        Assert.Single(PipelineLog.Entries);
        Assert.Equal("short-circuit", PipelineLog.Entries[0]);
    }
}

public class CancellationPropagationTests
{
    [Fact]
    public async Task Send_TokenReachesHandler()
    {
        CancellationCapturingHandler.Captured = null;
        var sp = new TestServiceProvider();
        sp.Register(typeof(IRequestHandler<Ping, string>), new CancellationCapturingHandler());
        var mediator = new Mediator(sp);

        using var cts = new CancellationTokenSource();
        await mediator.Send(new Ping { Message = "p" }, cts.Token);

        Assert.NotNull(CancellationCapturingHandler.Captured);
        Assert.Equal(cts.Token, CancellationCapturingHandler.Captured!.Value);
    }

    [Fact]
    public async Task Send_TokenReachesPipelineBehavior()
    {
        CancellationCapturingBehavior.Captured = null;
        var sp = new TestServiceProvider();
        sp.Register(typeof(IRequestHandler<Ping, string>), new PingHandler());
        sp.RegisterCollection(typeof(IPipelineBehavior<Ping, string>),
            new object[] { new CancellationCapturingBehavior() });
        var mediator = new Mediator(sp);

        using var cts = new CancellationTokenSource();
        await mediator.Send(new Ping { Message = "p" }, cts.Token);

        Assert.NotNull(CancellationCapturingBehavior.Captured);
        Assert.Equal(cts.Token, CancellationCapturingBehavior.Captured!.Value);
    }
}
