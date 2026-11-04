using System.Net;
using Ambinder.A2A.Benchmarks.Data;
using Ambinder.A2A.Benchmarks.Hosts;
using BenchmarkDotNet.Attributes;

namespace Ambinder.A2A.Benchmarks.Request;

/// <summary>
/// The control: a hand-written minimal API on the same <c>TestServer</c> that reads a <c>SendMessageRequest</c> and writes a canned
/// response. The name is <c>Control</c> so the markdown table shows it next to the server-only rows.
/// </summary>
[BenchmarkCategory(Categories.Request)]
public class ControlBenchmarks
{
    private BenchServer _server = null!;
    private HttpClient _http = null!;
    private byte[] _body = null!;

    [GlobalSetup]
    public void Setup()
    {
        _server = BenchServer.StartTestServerAsync(ControlEndpoint.Map).GetAwaiter().GetResult();
        _http = _server.InMemoryClient!;
        _body = Payloads.SendMessageRequestBytes;
        Control().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => _server.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Benchmark]
    public async Task Control()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ControlEndpoint.Path) { Content = new ByteArrayContent(_body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK || !BenchServer.Contains(bytes, "Hello from the benchmark agent."u8))
        {
            throw new InvalidOperationException($"Control answered {(int)response.StatusCode} without the canned reply.");
        }
    }
}
