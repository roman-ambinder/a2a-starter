using System.Text.Json;
using A2A.Protocol;
using Microsoft.AspNetCore.Http;

namespace A2A.Server.Transports;

/// <summary>Reads the A2A service parameters (version and extensions) from an HTTP request.</summary>
public static class ServiceParameters
{
    private static readonly JsonElement SupportedVersionsData =
        JsonDocument.Parse($$"""{"supportedVersions":["{{A2AConstants.ProtocolVersion}}"]}""").RootElement.Clone();

    /// <summary>
    /// Version: the <c>A2A-Version</c> header, else the <c>A2A-Version</c> query parameter (trimmed; empty → null).
    /// Extensions: the <c>A2A-Extensions</c> header values, comma-separated, trimmed, empty entries dropped.
    /// </summary>
    public static (string? Version, IReadOnlyList<string> Extensions) Read(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var version = FirstNonEmpty(context.Request.Headers[A2AConstants.VersionHeader])
                      ?? FirstNonEmpty(context.Request.Query[A2AConstants.VersionHeader]);
        return (version, ParseExtensions(context.Request.Headers[A2AConstants.ExtensionsHeader]));
    }

    /// <summary>Splits comma-separated extension URI lists (several values allowed). Shared with gRPC metadata.</summary>
    public static IReadOnlyList<string> ParseExtensions(IEnumerable<string?> values)
        => values.SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Throws <see cref="A2AErrorCode.VersionNotSupported"/> (data <c>{"supportedVersions":["1.0"]}</c>) unless the version's
    /// <c>Major.Minor</c> is "1.0". A patch part (<c>1.0.1</c>) is ignored (spec 3.6). An empty value means A2A 0.3 (spec 3.6.2),
    /// which this server does not serve.
    /// </summary>
    public static void EnsureSupportedVersion(string? version)
    {
        if (IsSupportedVersion(version)) return;
        var message = string.IsNullOrEmpty(version)
            ? $"No {A2AConstants.VersionHeader} header was sent, so the request is treated as A2A 0.3. This agent supports " +
              $"{A2AConstants.ProtocolVersion}. Send the header '{A2AConstants.VersionHeader}: {A2AConstants.ProtocolVersion}'."
            : $"A2A version '{version}' is not supported. Supported: {A2AConstants.ProtocolVersion}. " +
              $"Send the header '{A2AConstants.VersionHeader}: {A2AConstants.ProtocolVersion}'.";
        throw new A2AException(A2AErrorCode.VersionNotSupported, message, SupportedVersionsData);
    }

    /// <summary>
    /// The requested version as compared against the supported set and passed to <see cref="Hosting.IA2AVersionDispatcher"/>:
    /// empty or whitespace → <c>0.3</c> (spec 3.6.2); <c>Major.Minor</c> or <c>Major.Minor.Patch</c> → <c>Major.Minor</c>
    /// (spaces trimmed, patch ignored); anything else is returned trimmed and unchanged, so it fails the supported check.
    /// </summary>
    public static string NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0.3";
        var trimmed = raw.Trim();
        var parts = trimmed.Split('.');
        if (parts.Length is < 2 or > 3) return trimmed;
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
                return trimmed;
        }
        return $"{numbers[0]}.{numbers[1]}";
    }

    /// <summary>True when <paramref name="raw"/> normalizes (see <see cref="NormalizeVersion"/>) to a version the core serves.</summary>
    internal static bool IsSupportedVersion(string? raw) => NormalizeVersion(raw) == A2AConstants.ProtocolVersion;

    /// <summary>
    /// Applies the extension rules of spec 3.3.4 / 4.6 for one operation on <paramref name="card"/>: throws
    /// <see cref="A2AErrorCode.ExtensionSupportRequired"/> when a <c>required</c> extension was not requested, and returns the
    /// activated list (requested ∩ declared). The transport echoes that list in <c>A2A-Extensions</c> / <c>a2a-extensions</c>.
    /// </summary>
    public static IReadOnlyList<string> ActivateExtensions(AgentCard card, IReadOnlyList<string> requested)
        => Tasks.RequestValidator.ResolveExtensions(card, requested);

    /// <summary>Activates extensions (see <see cref="ActivateExtensions"/>) and echoes them in the <c>A2A-Extensions</c> response header.</summary>
    public static void ActivateExtensions(HttpContext http, AgentCard card, IReadOnlyList<string> requested)
    {
        ArgumentNullException.ThrowIfNull(http);
        var active = ActivateExtensions(card, requested);
        if (active.Count > 0) http.Response.Headers[A2AConstants.ExtensionsHeader] = string.Join(',', active);
    }

    private static string? FirstNonEmpty(IEnumerable<string?> values)
        => values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
