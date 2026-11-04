namespace Ambinder.A2A.Client;

/// <summary>The result of a unary <see cref="IA2ATransport.SendAsync"/> call.</summary>
public sealed class A2ATransportResponse
{
    /// <summary>Gets the response record, for example a <c>SendMessageResponse</c>.</summary>
    public required object Body { get; init; }

    /// <summary>Gets facts about the response.</summary>
    public required A2AResponseInfo Info { get; init; }
}
