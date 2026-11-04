using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>A stream ended before the agent returned a task id, so the client cannot resume it.</summary>
public class A2AStreamInterruptedException : A2AException
{
    /// <summary>Creates the exception with the standard message.</summary>
    public A2AStreamInterruptedException()
        : this("the stream ended before the agent returned a task; the message may or may not have been accepted.")
    {
    }

    /// <summary>Creates the exception with a custom message.</summary>
    /// <param name="message">The message that describes the failure.</param>
    public A2AStreamInterruptedException(string message)
        : base(A2AErrorCode.InvalidAgentResponse, message)
    {
    }
}
