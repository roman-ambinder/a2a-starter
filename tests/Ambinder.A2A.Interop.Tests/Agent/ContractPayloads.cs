using System.Text.Json;

namespace Ambinder.A2A.Interop.Tests.Agent;

/// <summary>
/// The fixed payloads of the interop agent commands <c>data</c> and <c>raw</c>, and the other fixed values of the contract.
/// <c>interop/contract.md</c> quotes these literals; change both together. Every peer server must return exactly these values.
/// </summary>
public static class ContractPayloads
{
    /// <summary>Name of the main agent (card at <c>/.well-known/agent-card.json</c>).</summary>
    public const string MainAgent = "interop";

    /// <summary>Name of the second agent, whose card declares <see cref="RequiredExtension"/> as required.</summary>
    public const string RequiredExtensionAgent = "required-ext";

    /// <summary>Name of the agent whose interfaces all carry <see cref="Tenant"/>.</summary>
    public const string TenantAgent = "tenant-agent";

    /// <summary>Extension URI the second agent requires.</summary>
    public const string RequiredExtension = "urn:a2a:interop:required";

    /// <summary>Tenant of every interface of <see cref="TenantAgent"/>.</summary>
    public const string Tenant = "interop-tenant";

    /// <summary>Fixed test bearer token that guards the extended card. Not a secret.</summary>
    public const string BearerToken = "interop-test-token";

    /// <summary>The <c>data</c> command returns one message with one data part holding this JSON object.</summary>
    public const string DataJson = """
        {
          "integer": 42,
          "negativeInteger": -7,
          "zero": 0,
          "float": 3.14159,
          "negativeFloat": -0.5,
          "smallFloat": 0.000001,
          "maxSafeInteger": 9007199254740991,
          "minSafeInteger": -9007199254740991,
          "unicode": "héllo wörld ✓ 日本語 😀",
          "escapes": "line1\nline2\t\"quoted\" back\\slash",
          "emptyString": "",
          "true": true,
          "false": false,
          "null": null,
          "array": [1, 2.5, "three", null, [4, 5], {"six": 6}],
          "nested": {"a": {"b": {"c": [true, null, {"d": "deep"}]}}, "empty": {}, "emptyArray": []}
        }
        """;

    /// <summary>The <c>raw</c> command returns one message with one raw part holding these bytes.</summary>
    public static ReadOnlySpan<byte> RawBytes => [0x00, 0x01, 0x02, 0x03, 0xFC, 0xFD, 0xFE, 0xFF];

    /// <summary>The raw bytes as they appear on the wire.</summary>
    public const string RawBase64 = "AAECA/z9/v8=";

    /// <summary>File name of the raw part.</summary>
    public const string RawFilename = "interop.bin";

    /// <summary>Media type of the raw part.</summary>
    public const string RawMediaType = "application/octet-stream";

    /// <summary>A fresh copy of <see cref="DataJson"/> as a <see cref="JsonElement"/>.</summary>
    public static JsonElement DataElement()
    {
        using var doc = JsonDocument.Parse(DataJson);
        return doc.RootElement.Clone();
    }
}
