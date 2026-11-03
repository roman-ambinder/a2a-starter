using System.Text;
using System.Text.RegularExpressions;

namespace AgentClient;

/// <summary>
/// Prints every request and response (method, URL, headers, body). Secret header values and token fields are masked.
/// Streaming bodies (SSE, gRPC) are not buffered, so they are listed without a body.
/// </summary>
public sealed partial class WireLogHandler(TextWriter output, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private static readonly HashSet<string> SecretHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "X-API-Key", "X-A2A-Notification-Token", "X-A2A-Push-Signature",
    };

    private const string Mask = "***";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append("  > ").Append(request.Method).Append(' ').AppendLine(request.RequestUri?.ToString());
        AppendHeaders(sb, "  > ", request.Headers);
        if (request.Content is { } content)
        {
            AppendHeaders(sb, "  > ", content.Headers);
            if (IsStream(content.Headers.ContentType?.MediaType)) sb.AppendLine("  > <stream body not logged>");
            else
            {
                await content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
                AppendBody(sb, "  > ", await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
        }
        Write(sb);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        sb.Clear();
        sb.Append("  < ").Append((int)response.StatusCode).Append(' ').AppendLine(response.ReasonPhrase);
        AppendHeaders(sb, "  < ", response.Headers);
        AppendHeaders(sb, "  < ", response.Content.Headers);
        if (IsStream(response.Content.Headers.ContentType?.MediaType)) sb.AppendLine("  < <stream body not logged>");
        else
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            AppendBody(sb, "  < ", await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        Write(sb);
        return response;
    }

    private void Write(StringBuilder sb)
    {
        lock (output) output.Write(sb.ToString());
    }

    private static bool IsStream(string? mediaType)
        => mediaType is not null && (mediaType.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase));

    private static void AppendHeaders(StringBuilder sb, string prefix, System.Net.Http.Headers.HttpHeaders headers)
    {
        foreach (var (name, values) in headers)
            sb.Append(prefix).Append(name).Append(": ").AppendLine(MaskHeader(name, string.Join(", ", values)));
    }

    private static void AppendBody(StringBuilder sb, string prefix, string body)
    {
        if (body.Length == 0) return;
        sb.Append(prefix).AppendLine(MaskBody(body));
    }

    /// <summary>Masks the value of a secret header; keeps the scheme word of <c>Authorization</c>.</summary>
    public static string MaskHeader(string name, string value)
    {
        if (!SecretHeaders.Contains(name)) return value;
        var space = value.IndexOf(' ');
        return name.EndsWith("Authorization", StringComparison.OrdinalIgnoreCase) && space > 0 ? value[..space] + " " + Mask : Mask;
    }

    /// <summary>Masks secrets in a form or JSON body: <c>client_secret</c>, <c>access_token</c> and similar, push credentials and tokens.</summary>
    public static string MaskBody(string body)
    {
        body = FormSecret().Replace(body, "$1" + Mask);
        body = JsonSecret().Replace(body, "\"$1\":\"" + Mask + "\"");
        return body;
    }

    [GeneratedRegex("(client_secret=|access_token=|refresh_token=|assertion=)[^&\\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex FormSecret();

    [GeneratedRegex("\"(client_secret|access_token|refresh_token|id_token|credentials|token)\"\\s*:\\s*\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecret();
}
