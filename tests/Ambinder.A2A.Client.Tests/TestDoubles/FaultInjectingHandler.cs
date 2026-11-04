using System.Net;

namespace Ambinder.A2A.Client.Tests.TestDoubles;

/// <summary>
/// Plays a queue of steps, one per attempt. A step returns a response or throws. When the queue is empty the last step repeats.
/// </summary>
public sealed class FaultInjectingHandler(params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] steps) : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _steps = new(steps);
    private Func<HttpRequestMessage, Task<HttpResponseMessage>>? _last;
    private int _attempts;

    /// <summary>How many attempts reached the handler.</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>A step that answers with <paramref name="status"/> and optional headers.</summary>
    public static Func<HttpRequestMessage, Task<HttpResponseMessage>> Status(HttpStatusCode status,
        Action<HttpResponseMessage>? configure = null) => _ =>
    {
        var response = new HttpResponseMessage(status);
        configure?.Invoke(response);
        return Task.FromResult(response);
    };

    /// <summary>A step that answers with the given response factory.</summary>
    public static Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond(Func<HttpResponseMessage> make) => _ => Task.FromResult(make());

    /// <summary>A step that fails before any byte was sent.</summary>
    public static Func<HttpRequestMessage, Task<HttpResponseMessage>> ConnectFailure() =>
        _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "connect failed");

    /// <summary>A step that reads the request content, then fails: the bytes were sent.</summary>
    public static Func<HttpRequestMessage, Task<HttpResponseMessage>> FailAfterSend() => async request =>
    {
        if (request.Content is not null) await request.Content.ReadAsByteArrayAsync();
        throw new IOException("connection lost after the request was sent");
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _attempts);
        Func<HttpRequestMessage, Task<HttpResponseMessage>> step;
        lock (_steps)
        {
            step = _steps.Count > 0 ? _last = _steps.Dequeue() : _last ?? throw new InvalidOperationException("No steps scripted.");
        }
        var response = await step(request);
        response.RequestMessage ??= request;
        return response;
    }
}
