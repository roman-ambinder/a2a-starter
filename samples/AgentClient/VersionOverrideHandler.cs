namespace AgentClient;

/// <summary>
/// Replaces the <c>A2A-Version</c> header (HTTP, or gRPC metadata, which travels as the same header) on every request.
/// Only the <c>errors</c> flow uses it, to show how an agent answers a version it does not serve: a normal client always sends
/// the version of the card interface it selected.
/// </summary>
public sealed class VersionOverrideHandler(string version, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("A2A-Version");
        request.Headers.TryAddWithoutValidation("A2A-Version", version);
        return base.SendAsync(request, cancellationToken);
    }
}
