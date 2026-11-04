namespace Ambinder.A2A.AspNetCore.Transports;

/// <summary>
/// EventIds for the log messages in the shared transport files (src/Shared). EventIds are public API. This assembly owns 3000 to 3999:
/// each assembly that links src/Shared/TransportErrors.cs defines this class with its own numbers.
/// </summary>
internal static class LogEventIds
{
    public const int UnhandledTransportError = 3001;
}
