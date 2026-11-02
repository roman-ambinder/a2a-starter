using System.Text.Json;
using A2A.Integration.Tests.Fixtures;
using A2A.Protocol;

namespace A2A.Integration.Tests.Flows;

[Collection(A2ATestHostCollection.Name)]
public sealed class EchoFlowTests(A2ATestHost host)
{
    private const string TimestampExtension = "https://example.com/a2a/ext/timestamp/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SendMessageRequest Send(string? contextId, params Part[] parts) => new()
    {
        Message = new Message { MessageId = Guid.NewGuid().ToString("N"), ContextId = contextId, Role = Role.User, Parts = parts },
    };

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Echo_returns_direct_message_for_text_file_and_data_parts(string binding)
    {
        await using var client = await host.CreateClientAsync("echo", binding, ct: Ct);
        var data = JsonSerializer.SerializeToElement(new { a = 1, b = new[] { "x", "y" } });
        var raw = Part.FromRaw([1, 2, 3, 4], "application/octet-stream", "blob.bin");
        var url = Part.FromUrl("https://example.com/files/doc.pdf", "application/pdf", "doc.pdf");
        var contextId = Guid.NewGuid().ToString();

        var response = await client.SendMessageAsync(Send(contextId, Part.FromText("hello"), raw, url, Part.FromData(data)), Ct);

        Assert.Null(response.Task);
        var reply = Assert.IsType<Message>(response.Message);
        Assert.Equal(Role.Agent, reply.Role);
        Assert.Equal(contextId, reply.ContextId);
        Assert.Equal(4, reply.Parts.Count);
        Assert.Equal("echo: hello", reply.Parts[0].Text);
        Assert.Equal<byte>([1, 2, 3, 4], reply.Parts[1].Raw!);
        Assert.Equal(("application/octet-stream", "blob.bin"), (reply.Parts[1].MediaType, reply.Parts[1].Filename));
        Assert.Equal(("https://example.com/files/doc.pdf", "application/pdf", "doc.pdf"), (reply.Parts[2].Url, reply.Parts[2].MediaType, reply.Parts[2].Filename));
        Assert.True(JsonElement.DeepEquals(data, reply.Parts[3].Data!.Value));
        Assert.Empty(reply.Extensions);
    }

    [Theory]
    [MemberData(nameof(TransportMatrix.Bindings), MemberType = typeof(TransportMatrix))]
    public async Task Echo_timestamp_extension_adds_metadata_only_when_requested(string binding)
    {
        await using var plain = await host.CreateClientAsync("echo", binding, ct: Ct);
        var without = (await plain.SendMessageAsync(Send(null, Part.FromText("hi")), Ct)).Message!;
        Assert.Empty(without.Extensions);
        Assert.True(without.Metadata is null || !without.Metadata.Value.TryGetProperty(TimestampExtension, out _));

        await using var withExt = await host.CreateClientAsync("echo", binding, extensions: [TimestampExtension], ct: Ct);
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var reply = (await withExt.SendMessageAsync(Send(null, Part.FromText("hi")), Ct)).Message!;

        Assert.Equal([TimestampExtension], reply.Extensions);
        var stamp = reply.Metadata!.Value.GetProperty(TimestampExtension).GetProperty("timestamp").GetString()!;
        var parsed = DateTimeOffset.Parse(stamp, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.InRange(parsed, before, DateTimeOffset.UtcNow.AddSeconds(5));
    }
}
