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

    /// <summary>Throws <see cref="A2AErrorCode.VersionNotSupported"/> (data <c>{"supportedVersions":["1.0"]}</c>) unless the version is "1.0".</summary>
    public static void EnsureSupportedVersion(string? version)
    {
        if (version == A2AConstants.ProtocolVersion) return;
        var message = version is null
            ? $"The {A2AConstants.VersionHeader} header (or query parameter) is required; supported: {A2AConstants.ProtocolVersion}."
            : $"A2A version '{version}' is not supported; supported: {A2AConstants.ProtocolVersion}.";
        throw new A2AException(A2AErrorCode.VersionNotSupported, message, SupportedVersionsData);
    }

    private static string? FirstNonEmpty(IEnumerable<string?> values)
        => values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
