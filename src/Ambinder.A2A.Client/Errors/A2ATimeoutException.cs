using System.Globalization;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client;

/// <summary>An operation ran past its time limit. Caller cancellation never throws this; it throws <see cref="OperationCanceledException"/>.</summary>
public class A2ATimeoutException : A2AException
{
    /// <summary>Creates the exception and builds the message.</summary>
    /// <param name="operation">The operation that timed out, for example <c>SendMessage (blocking)</c>.</param>
    /// <param name="elapsed">How long the operation ran.</param>
    /// <param name="option">The option to raise, for example <c>A2AClientOptions.Timeouts.BlockingSend</c>.</param>
    /// <param name="alternative">Another way out, for example <c>use SendAndWaitAsync</c>, or <see langword="null"/>.</param>
    public A2ATimeoutException(string operation, TimeSpan elapsed, string option, string? alternative = null)
        : base(A2AErrorCode.InternalError, BuildMessage(operation, elapsed, option, alternative))
    {
        Operation = operation;
        Elapsed = elapsed;
        Option = option;
    }

    /// <summary>The operation that timed out.</summary>
    public string Operation { get; }

    /// <summary>How long the operation ran.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>The option to raise.</summary>
    public string Option { get; }

    private static string BuildMessage(string operation, TimeSpan elapsed, string option, string? alternative)
    {
        var time = elapsed.TotalSeconds >= 60 && elapsed.Ticks % TimeSpan.TicksPerMinute == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{(long)elapsed.TotalMinutes} min")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(elapsed.TotalSeconds, 1):0.#} s");
        var or = alternative is null ? "" : $" or {alternative}";
        return $"{operation} timed out after {time}; raise {option}{or}.";
    }
}
