using System.Net;
using System.Text;
using System.Text.Json;

namespace Ambinder.A2A.Client.Tests.TestDoubles;

/// <summary>Answers each request with a scripted response and records what was sent.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<byte[]> Bodies { get; } = [];
    public int Calls => Requests.Count;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((r, _) => Task.FromResult(respond(r))) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Bodies.Add(request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct));
        Requests.Add(request);
        var response = await respond(request, ct);
        response.RequestMessage ??= request;
        return response;
    }

    /// <summary>A 200 response with a JSON body.</summary>
    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    /// <summary>A 200 JSON-RPC envelope that carries <paramref name="result"/> (id 1).</summary>
    public static HttpResponseMessage JsonRpcOk(object result) => Json(JsonRpcResult(result));

    /// <summary>The JSON-RPC envelope text for <paramref name="result"/> (a JSON string is embedded as is).</summary>
    public static string JsonRpcResult(object result)
    {
        var json = result as string ?? JsonSerializer.Serialize(result);
        return $$"""{"jsonrpc":"2.0","id":1,"result":{{json}}}""";
    }
}
