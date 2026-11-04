using System.Diagnostics.CodeAnalysis;
using Ambinder.A2A.Server.Hosting;

namespace Ambinder.A2A.Server.Agents;

/// <summary>Settings for one agent, given to the callback of <c>AddAgent</c>.</summary>
/// <remarks>The builder creates and fills it at startup, on one thread. It is not used after that.</remarks>
public sealed class AgentOptions
{
    private readonly List<Type> _middleware = [];
    private readonly A2AServerBuilder _builder;

    internal AgentOptions(A2AServerBuilder builder) => _builder = builder;

    internal IReadOnlyList<Type> MiddlewareTypes => _middleware;

    /// <summary>Adds a middleware for this agent only. It runs after the global middleware, in registration order.</summary>
    /// <typeparam name="T">The middleware type. It is registered as scoped if you have not registered it.</typeparam>
    /// <returns>These options, so calls can be chained.</returns>
    public AgentOptions UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IA2AMiddleware
    {
        _builder.RegisterMiddlewareType<T>();
        _middleware.Add(typeof(T));
        return this;
    }
}
