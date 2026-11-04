using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;
using Ambinder.A2A.Server.Tasks;

namespace Ambinder.A2A.Server;

/// <summary>
/// Keys for <see cref="A2AOperationContext.Items"/> that the SDK's own middleware sets.
/// </summary>
public static class A2AExtensionsKeys
{
    /// <summary>
    /// The key of the extensions that are active for this call: the requested extensions that the agent card declares
    /// (<c>IReadOnlyList&lt;string&gt;</c>, in request order). Set by the extension negotiation middleware (slot 400), so middleware that runs
    /// after it (slot 400 and later, that is, app middleware) can read it. It is not set for forwarded calls.
    /// </summary>
    public const string Active = "Ambinder.A2A.Extensions.Active";
}

/// <summary>
/// Part of an extension negotiation that runs once the active list is known. Package 11 adds implementations in M2; the list is empty until then.
/// </summary>
internal interface IExtensionHook
{
    ValueTask OnNegotiatedAsync(A2AOperationContext context, IReadOnlyList<string> active);
}

/// <summary>
/// Built-in middleware in slot 400 (pipeline step 4): checks that every <c>required</c> extension of the agent card was requested
/// (<see cref="A2AErrorCode.ExtensionSupportRequired"/> otherwise), computes the active list (requested and declared), puts it on
/// <see cref="A2AOperationContext.Items"/> under <see cref="A2AExtensionsKeys.Active"/> and echoes it in the <c>A2A-Extensions</c>
/// response metadata. The terminal step hands the list to the task manager through <see cref="A2ACallContext.ActiveExtensions"/>.
/// It does not run for forwarded calls: the instance that forwarded the call has negotiated already.
/// </summary>
internal sealed class ExtensionNegotiationMiddleware(AgentCatalog catalog, IEnumerable<IExtensionHook> hooks) : IA2AMiddleware
{
    public async ValueTask<A2AOperationResult> InvokeAsync(A2AOperationContext context, A2AOperationDelegate next)
    {
        // An unknown agent is reported by the operation itself.
        if (catalog.TryGet(context.Call.AgentName, out var agent))
        {
            var active = RequestValidator.ResolveExtensions(agent.PublicCard, context.Call.RequestedExtensions);
            context.Items[A2AExtensionsKeys.Active] = active;
            if (active.Count > 0)
                context.ResponseMetadata.Set(A2AConstants.ExtensionsHeader, string.Join(',', active));
            foreach (var hook in hooks)
                await hook.OnNegotiatedAsync(context, active).ConfigureAwait(false);
        }
        return await next(context).ConfigureAwait(false);
    }
}
