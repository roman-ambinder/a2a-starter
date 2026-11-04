using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Hosting;

/// <summary>Registers the hosted agents. Returned by <see cref="Microsoft.Extensions.DependencyInjection.A2AServerCoreServiceCollectionExtensions.AddA2AServerCore"/>.</summary>
/// <remarks>The builder is for startup only. Add all agents before the host starts; the catalog is built once, on first use.</remarks>
public sealed partial class A2AServerBuilder
{
    private readonly List<Definition> _agents = [];
    private bool _authorizationMiddleware;

    internal A2AServerBuilder(IServiceCollection services) => Services = services;

    internal A2APipelineMiddleware Middleware { get; } = new();

    /// <summary>The service collection the builder adds to. Use it to register your own services for your agents.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Adds an agent. The card factories run once, when the <see cref="AgentCatalog"/> is first resolved.
    /// Hosting replaces the <c>supportedInterfaces</c> of both cards with the generated JSON-RPC, REST and
    /// (when <see cref="A2AServerOptions.GrpcPorts"/> has the agent) gRPC interfaces; whatever the factory put there is ignored.
    /// </summary>
    /// <param name="name">URL-safe agent name (letters, digits, <c>-</c>, <c>_</c>, <c>.</c>).</param>
    /// <param name="publicCard">The anonymous card.</param>
    /// <param name="extendedCard">The extended card for authenticated callers. Also set <c>capabilities.extendedAgentCard</c> on the public card.</param>
    /// <param name="authorizationPolicy">ASP.NET Core policy every operation of this agent must pass (cards excepted). Null → open.</param>
    /// <param name="configure">Sets the <see cref="AgentOptions"/> of this agent, for example to add middleware for this agent only. It runs now, once. Null → no settings.</param>
    /// <typeparam name="THandler">The handler type. The server creates a new instance for each execution with dependency injection, so the type may take registered services in its constructor.</typeparam>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentException">The name is empty, has other characters than letters, digits, <c>-</c>, <c>_</c> or <c>.</c>, or is already registered.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="publicCard"/> is <see langword="null"/>.</exception>
    /// <remarks>This method registers no services itself. The handler type is created through the container when the agent runs, and its dependencies must be registered by you.</remarks>
    /// <example>
    /// <code>
    /// // requires: IServiceCollection services;
    /// services.AddA2AServer(o => o.PublicBaseUrl = "https://agents.contoso.com")
    ///     .AddAgent&lt;EchoAgent&gt;("echo", sp => new AgentCard
    ///     {
    ///         Name = "Echo",
    ///         Description = "Repeats what you say.",
    ///         Version = "1.0.0",
    ///         Skills = [new AgentSkill { Id = "echo", Name = "Echo", Description = "Repeats text." }],
    ///     });
    /// </code>
    /// </example>
    public A2AServerBuilder AddAgent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(string name, Func<IServiceProvider, AgentCard> publicCard,
        Func<IServiceProvider, AgentCard>? extendedCard = null, string? authorizationPolicy = null, Action<AgentOptions>? configure = null)
        where THandler : class, IAgentHandler
        => AddAgentCore<THandler>(name, publicCard, extendedCard, authorizationPolicy, configure);

    /// <summary>Adds a middleware that runs for every operation of every agent, after the built-in middleware and before per-agent middleware.</summary>
    /// <typeparam name="T">The middleware type. It is registered as scoped if you have not registered it.</typeparam>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <remarks>Middleware runs in registration order. See <see cref="IA2AMiddleware"/>.</remarks>
    public A2AServerBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IA2AMiddleware
    {
        RegisterMiddlewareType<T>();
        Middleware.AddGlobal(typeof(T));
        return this;
    }

    /// <summary>Sets the authorization middleware. It runs after limits and before extension negotiation.</summary>
    /// <typeparam name="T">The middleware type. It is registered as scoped if you have not registered it.</typeparam>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="InvalidOperationException">An authorization middleware is already set.</exception>
    /// <remarks>For the authorization package of the SDK; one only. Apps use <see cref="UseMiddleware{T}"/>.</remarks>
    public A2AServerBuilder UseAuthorizationMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IA2AMiddleware
    {
        if (_authorizationMiddleware)
            throw new InvalidOperationException("An authorization middleware is already set. Only one can be used; register your own checks with UseMiddleware<T>() instead.");
        _authorizationMiddleware = true;
        AddBuiltInMiddleware(A2APipelineMiddleware.AuthorizationSlot, typeof(T));
        return this;
    }

    internal void AddBuiltInMiddleware(int slot, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
    {
        Services.TryAddScoped(type);
        Middleware.AddBuiltIn(slot, type);
    }

    internal void RegisterMiddlewareType<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IA2AMiddleware => Services.TryAddScoped<T>();

    private A2AServerBuilder AddAgentCore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(string name,
        Func<IServiceProvider, AgentCard> publicCard, Func<IServiceProvider, AgentCard>? extendedCard, string? authorizationPolicy, Action<AgentOptions>? configure)
        where THandler : class, IAgentHandler
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(publicCard);
        if (!NamePattern().IsMatch(name))
            throw new ArgumentException($"Agent name '{name}' must contain only letters, digits, '-', '_' or '.'.", nameof(name));
        if (_agents.Any(a => a.Name == name))
            throw new ArgumentException($"Agent '{name}' is registered twice.", nameof(name));
        var options = new AgentOptions(this);
        configure?.Invoke(options);
        _agents.Add(new Definition(name, typeof(THandler), publicCard, extendedCard, authorizationPolicy, options.MiddlewareTypes));
        return this;
    }

    internal AgentCatalog BuildCatalog(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<A2AServerOptions>>().Value;
        var registrations = _agents.Select(a => new AgentRegistration(
            a.Name,
            a.HandlerType,
            AgentCardInterfaces.Apply(a.PublicCard(services), a.Name, options),
            a.ExtendedCard is null ? null : AgentCardInterfaces.Apply(a.ExtendedCard(services), a.Name, options),
            a.AuthorizationPolicy) { MiddlewareTypes = a.Middleware }).ToList();
        if (options.DefaultAgent is { } d && registrations.All(r => r.Name != d))
            throw new InvalidOperationException(
                $"A2AServerOptions.DefaultAgent is '{d}', which is not a registered agent (registered: {(registrations.Count == 0 ? "none" : string.Join(", ", registrations.Select(r => $"'{r.Name}'")))}). " +
                "Set DefaultAgent to the name of an agent you added with AddAgent, or leave it null to serve no card at /.well-known/agent-card.json.");
        return new AgentCatalog(registrations);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex NamePattern();

    private sealed record Definition(string Name,
        [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type HandlerType, Func<IServiceProvider, AgentCard> PublicCard,
        Func<IServiceProvider, AgentCard>? ExtendedCard, string? AuthorizationPolicy, IReadOnlyList<Type> Middleware);
}
