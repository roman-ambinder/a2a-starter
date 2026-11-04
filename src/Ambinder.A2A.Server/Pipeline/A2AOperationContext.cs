namespace Ambinder.A2A.Server;

/// <summary>What a middleware sees of one operation.</summary>
/// <remarks>One instance serves one call. It is not thread-safe; use it from the call's own flow.</remarks>
public sealed class A2AOperationContext
{
    private Dictionary<object, object?>? _items;
    private object _request;

    internal A2AOperationContext(A2AOperation operation, A2ACallContext call, object request, IServiceProvider services, A2AResponseMetadata metadata, CancellationToken requestAborted)
    {
        Operation = operation;
        Call = call;
        _request = request;
        Services = services;
        ResponseMetadata = metadata;
        RequestAborted = requestAborted;
    }

    /// <summary>Which of the A2A operations this call runs.</summary>
    public A2AOperation Operation { get; }

    /// <summary>The agent, caller, tenant, version, extensions and transport.</summary>
    public A2ACallContext Call { get; }

    /// <summary>
    /// The typed request model (for example <c>SendMessageRequest</c> for <see cref="A2AOperation.SendMessage"/>). A middleware may replace it, but the new value
    /// must have the same CLR type; the pipeline checks this before the operation runs.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public object Request
    {
        get => _request;
        set => _request = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Per-call storage for middleware to share data. It is created on first use.</summary>
    public IDictionary<object, object?> Items => _items ??= new();

    /// <summary>The headers (HTTP) or metadata (gRPC) written before the body. In process, a private instance that nothing sends.</summary>
    public A2AResponseMetadata ResponseMetadata { get; }

    /// <summary>The request scope.</summary>
    public IServiceProvider Services { get; }

    /// <summary>A token that is canceled when the caller goes away.</summary>
    public CancellationToken RequestAborted { get; }

    /// <summary><see langword="true"/> for <see cref="A2AOperation.SendStreamingMessage"/> and <see cref="A2AOperation.SubscribeToTask"/>.</summary>
    public bool IsStreaming => Operation is A2AOperation.SendStreamingMessage or A2AOperation.SubscribeToTask;
}
