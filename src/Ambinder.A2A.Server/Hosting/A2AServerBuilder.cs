using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Hosting;

/// <summary>Registers the hosted agents. Returned by <see cref="Microsoft.Extensions.DependencyInjection.A2AServerCoreServiceCollectionExtensions.AddA2AServerCore"/>.</summary>
/// <remarks>The builder is for startup only. Add all agents before the host starts; the catalog is built once, on first use.</remarks>
public sealed partial class A2AServerBuilder
{
    private readonly List<Definition> _agents = [];

    internal A2AServerBuilder(IServiceCollection services) => Services = services;

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
        Func<IServiceProvider, AgentCard>? extendedCard = null, string? authorizationPolicy = null)
        where THandler : class, IAgentHandler
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(publicCard);
        if (!NamePattern().IsMatch(name))
            throw new ArgumentException($"Agent name '{name}' must contain only letters, digits, '-', '_' or '.'.", nameof(name));
        if (_agents.Any(a => a.Name == name))
            throw new ArgumentException($"Agent '{name}' is registered twice.", nameof(name));
        _agents.Add(new Definition(name, typeof(THandler), publicCard, extendedCard, authorizationPolicy));
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
            a.AuthorizationPolicy)).ToList();
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
        Func<IServiceProvider, AgentCard>? ExtendedCard, string? AuthorizationPolicy);
}
