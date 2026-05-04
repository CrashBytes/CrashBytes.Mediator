using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace CrashBytes.Mediator;

/// <summary>
/// Extension methods for registering CrashBytes.Mediator with
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMediator"/> and scans the supplied marker types'
    /// assemblies for closed-generic implementations of
    /// <see cref="IRequestHandler{TRequest, TResponse}"/> and
    /// <see cref="INotificationHandler{TNotification}"/>. Each handler is
    /// registered as a transient service against every closed-generic
    /// interface it implements.
    /// </summary>
    /// <remarks>
    /// Pipeline behaviors (<see cref="IPipelineBehavior{TRequest, TResponse}"/>)
    /// are intentionally NOT auto-registered. Order matters and is deliberate;
    /// call <c>services.AddTransient(typeof(IPipelineBehavior&lt;,&gt;), typeof(MyBehavior&lt;,&gt;))</c>
    /// in the order you want them to wrap.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="markerTypes">Types whose containing assemblies will be scanned. At least one must be supplied.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="markerTypes"/> is empty.</exception>
    public static IServiceCollection AddCrashBytesMediator(this IServiceCollection services, params Type[] markerTypes)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (markerTypes is null || markerTypes.Length == 0)
            throw new ArgumentException("At least one marker type is required to identify the assemblies to scan.", nameof(markerTypes));

        var assemblies = markerTypes.Select(t => t.Assembly).Distinct().ToArray();
        return AddCrashBytesMediator(services, assemblies);
    }

    /// <summary>
    /// Registers <see cref="IMediator"/> and scans the supplied assemblies for
    /// handlers. See <see cref="AddCrashBytesMediator(IServiceCollection, Type[])"/>
    /// for details.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies to scan for handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCrashBytesMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (assemblies is null || assemblies.Length == 0)
            throw new ArgumentException("At least one assembly is required.", nameof(assemblies));

        services.AddTransient<IMediator, Mediator>();

        foreach (var assembly in assemblies.Distinct())
        {
            RegisterHandlersFromAssembly(services, assembly);
        }

        return services;
    }

    private static void RegisterHandlersFromAssembly(IServiceCollection services, Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types may fail to load (e.g. missing optional dependencies);
            // proceed with the ones we can see.
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;

            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType) continue;
                var def = iface.GetGenericTypeDefinition();
                if (def == typeof(IRequestHandler<,>) || def == typeof(INotificationHandler<>))
                {
                    services.AddTransient(iface, type);
                }
            }
        }
    }
}
