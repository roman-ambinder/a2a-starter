namespace Ambinder.A2A.Protocol.Signing;

/// <summary>
/// The JSON value cannot be written in RFC 8785 (JCS) form: for example a lone surrogate, a duplicate member name,
/// or a number that JCS cannot keep exactly.
/// </summary>
/// <param name="message">The message that says what cannot be canonicalized and what to change.</param>
public sealed class JsonCanonicalizationException(string message) : FormatException(message);
