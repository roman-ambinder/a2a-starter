using System.Text;
using System.Text.Json;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Push;
using Ambinder.A2A.Server.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Ambinder.A2A.Server.Tests.Push;

public sealed class PushPayloadTests
{
    private static readonly StoredTask Task = new("agent", "owner", null, new AgentTask { Id = "t1" });

    private static StreamResponse Event() => new() { Task = new AgentTask { Id = "t1", ContextId = "c1" } };

    private static TaskPushNotificationConfig Config(string id = "cfg") => new()
    {
        Id = id,
        TaskId = "t1",
        Url = "https://hooks.example.com/a",
        Token = "tok-SECRET-123",
        Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = "cred-SECRET-456" },
    };

    [Fact]
    public void Default_formatter_writes_stream_response_as_a2a_json()
    {
        var evt = Event();
        var payload = new DefaultPushPayloadFormatter().Format(Task, evt, Config());
        Assert.Equal("application/a2a+json", payload.ContentType);
        var back = JsonSerializer.Deserialize(payload.Body.Span, A2AJson.StreamResponse);
        Assert.Equal(evt.Task!.Id, back!.Task!.Id);
        Assert.Equal(evt.Task.ContextId, back.Task.ContextId);
        Assert.Equal(JsonSerializer.Serialize(evt, A2AJson.StreamResponse), Encoding.UTF8.GetString(payload.Body.Span));
    }

    [Fact]
    public void Default_formatter_only_formats_1_0()
    {
        var f = new DefaultPushPayloadFormatter();
        Assert.True(f.CanFormat("1.0"));
        Assert.False(f.CanFormat("0.3"));
        Assert.False(f.CanFormat(""));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(1024 * 1024, false)]
    [InlineData(1024 * 1024, true)]
    public void Envelope_round_trips_bytes_exactly(int size, bool isFinal)
    {
        var body = new byte[size];
        new Random(size + 7).NextBytes(body);
        var written = PushEnvelope.Write(new PushEnvelope(1, "cfg-1", isFinal, "application/a2a+json", body));
        var read = PushEnvelope.Read(written);
        Assert.Equal(1, read.V);
        Assert.Equal("cfg-1", read.ConfigId);
        Assert.Equal(isFinal, read.IsFinal);
        Assert.Equal("application/a2a+json", read.ContentType);
        Assert.True(body.AsSpan().SequenceEqual(read.Body.Span));
    }

    [Fact]
    public void Envelope_contains_no_credentials()
    {
        var config = Config();
        var payload = new DefaultPushPayloadFormatter().Format(Task, Event(), config);
        var bytes = PushEnvelope.Write(new PushEnvelope(1, config.Id!, false, payload.ContentType, payload.Body));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(config.Token!, text);
        Assert.DoesNotContain(config.Authentication!.Credentials!, text);
        Assert.DoesNotContain(config.Url, text);
    }

    [Theory]
    [InlineData("""{"v":2,"configId":"c","isFinal":false,"contentType":"x","body":""}""")]
    [InlineData("""{"v":0,"configId":"c","isFinal":false,"contentType":"x","body":""}""")]
    [InlineData("""{"configId":"c","isFinal":false,"contentType":"x","body":""}""")]
    public void Envelope_unknown_version_throws(string json)
        => Assert.Throws<InvalidDataException>(() => PushEnvelope.Read(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"v":1,"configId":"c"}""")]
    [InlineData("""{"v":1,"configId":"c","isFinal":false,"contentType":"x","body":"***"}""")]
    public void Envelope_malformed_throws_invalid_data(string json)
        => Assert.Throws<InvalidDataException>(() => PushEnvelope.Read(Encoding.UTF8.GetBytes(json)));

    private sealed class CountingFormatter : IPushPayloadFormatter
    {
        public int Calls;
        public bool CanFormat(string protocolVersion) => true;
        public PushPayload Format(StoredTask task, StreamResponse evt, TaskPushNotificationConfig config)
        {
            Calls++;
            return new PushPayload { Body = new byte[] { 1, 2, 3 }, ContentType = "x/y" };
        }
    }

    [Fact]
    public void Formatter_bytes_are_reused_across_configs()
    {
        var formatter = new CountingFormatter();
        var cache = new PushPayloadCache();
        var evt = Event();
        var a = cache.GetOrFormat(formatter, Task, evt, Config("a"));
        var b = cache.GetOrFormat(formatter, Task, evt, Config("b"));
        Assert.Equal(1, formatter.Calls);
        Assert.True(a.Body.Equals(b.Body)); // same backing array, offset and length
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(a.Body, out var sa));
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(b.Body, out var sb));
        Assert.Same(sa.Array, sb.Array);
    }

    [Fact]
    public void Formatter_cache_keeps_separate_bytes_per_formatter()
    {
        var one = new CountingFormatter();
        var two = new CountingFormatter();
        var cache = new PushPayloadCache();
        cache.GetOrFormat(one, Task, Event(), Config());
        cache.GetOrFormat(two, Task, Event(), Config());
        Assert.Equal(1, one.Calls);
        Assert.Equal(1, two.Calls);
    }

    [Fact]
    public void AddA2APush_registers_default_formatter_once_and_allows_more()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddA2APush();
        services.AddA2APush();
        services.AddSingleton<IPushPayloadFormatter, CountingFormatter>();
        using var sp = services.BuildServiceProvider();
        var formatters = sp.GetServices<IPushPayloadFormatter>().ToList();
        Assert.Single(formatters, f => f is DefaultPushPayloadFormatter);
        Assert.Equal(2, formatters.Count);
    }
}
