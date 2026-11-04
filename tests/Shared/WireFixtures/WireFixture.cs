namespace Ambinder.A2A.Tests.WireFixtures;

public enum FixtureExpect { Roundtrip, ReadOnly, MustReject }

/// <summary>Sidecar <c>&lt;case&gt;.meta.json</c>.</summary>
public sealed record FixtureMeta(
    string Source,          // "spec-example" | "hand-written" | "peer-capture:<peer>@<version>"
    string SpecSection,     // e.g. "4.1.1" or "5.5"
    FixtureExpect Expect,
    string? Type = null,    // proto message name; defaults to the folder name
    string? Binding = null, // errors only: "jsonrpc" | "rest" | "grpc"
    string? Note = null);

/// <param name="Id">Path relative to the catalog root without extension, '/' separated, e.g. "Task/completed-with-artifact".</param>
/// <param name="Group">First path segment: a proto message name, "enums" or "errors".</param>
public sealed record WireFixture(string Id, string Group, string JsonPath, FixtureMeta Meta)
{
    public string ProtoType => Meta.Type ?? Group;
    public string ReadJson() => File.ReadAllText(JsonPath);
    public override string ToString() => Id; // readable theory row names
}
