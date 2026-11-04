namespace Ambinder.A2A.Server.Security;

/// <summary>Security settings of the A2A server: caller token handling, tenants and the extended card.</summary>
/// <remarks>
/// With <c>AddA2AServer</c> the values are bound from the <c>A2A:Security</c> configuration section. Set them once at startup;
/// the options are read-only afterwards.
/// </remarks>
public sealed class A2ASecurityOptions
{
    /// <summary>
    /// Keep the raw bearer token of the request in <see cref="A2ACaller.AccessToken"/> for the agent run. Needed only to call another
    /// service on behalf of the caller (on-behalf-of). The token lives in memory for the run; it is never stored in a task, logged
    /// or printed. Default: <see langword="false"/>.
    /// </summary>
    public bool KeepCallerToken { get; set; }

    /// <summary>
    /// The tenants each agent serves, by agent name. A request tenant is accepted only when it is in the agent's list (or on the
    /// agent's card interfaces). An agent without an entry accepts only an empty tenant. Default: empty.
    /// </summary>
    public IDictionary<string, string[]> Tenants { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal);

    /// <summary>
    /// Optional binding of an A2A tenant to Entra tenant ids, by A2A tenant. When a tenant has an entry, a caller whose
    /// <see cref="A2ACaller.EntraTenantId"/> is not in the list gets HTTP 403. This lets one deployment serve several product
    /// tenants safely. Default: empty (no binding).
    /// </summary>
    public IDictionary<string, string[]> TenantBindings { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal);

    /// <summary>
    /// When <see langword="true"/> (the default), <c>GetExtendedAgentCard</c> returns only the skills whose security requirements the
    /// caller meets. Set it to <see langword="false"/> to return every skill to any authenticated caller.
    /// </summary>
    public bool FilterExtendedCardSkills { get; set; } = true;
}
