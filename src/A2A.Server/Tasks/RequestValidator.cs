using A2A.Protocol;

namespace A2A.Server.Tasks;

/// <summary>Checks an incoming message against the protocol rules and the agent card.</summary>
internal static class RequestValidator
{
    public static void ValidateHistoryLength(int? historyLength)
    {
        if (historyLength is < 0)
            throw new A2AException(A2AErrorCode.InvalidParams, "historyLength must not be negative. Send 0 or more, or omit it.");
    }

    /// <summary>Longest client <c>contextId</c> accepted (spec 3.4.1 lets the server reject ids it cannot accept).</summary>
    public const int MaxContextIdLength = 256;

    /// <summary>
    /// A client <c>contextId</c> is kept as given, never replaced (spec 3.4.1). One the server cannot accept is rejected:
    /// longer than <see cref="MaxContextIdLength"/> or containing control characters.
    /// </summary>
    public static void ValidateContextId(string? contextId)
    {
        if (string.IsNullOrEmpty(contextId)) return;
        if (contextId.Length > MaxContextIdLength)
            throw new A2AException(A2AErrorCode.InvalidParams,
                $"message.contextId is longer than {MaxContextIdLength} characters. Send a shorter id, or omit it to get a new context.");
        if (contextId.Any(char.IsControl))
            throw new A2AException(A2AErrorCode.InvalidParams,
                "message.contextId contains control characters. Send a printable id, or omit it to get a new context.");
    }

    public static void ValidateMessage(SendMessageRequest request, AgentCard card)
    {
        var message = request.Message;
        if (string.IsNullOrEmpty(message.MessageId))
            throw new A2AException(A2AErrorCode.InvalidParams, "message.messageId is required. Send a unique id for each message.");
        if (message.Role == Role.Unspecified)
            throw new A2AException(A2AErrorCode.InvalidParams, "message.role is missing or has an unknown value; use ROLE_USER.");
        if (message.Role != Role.User)
            throw new A2AException(A2AErrorCode.InvalidParams, "message.role must be ROLE_USER. Clients send user messages only.");
        if (message.Parts.Count == 0)
            throw new A2AException(A2AErrorCode.InvalidParams, "message.parts must not be empty. Send at least one part.");
        for (var i = 0; i < message.Parts.Count; i++)
        {
            if (!message.Parts[i].IsValid)
                throw new A2AException(A2AErrorCode.InvalidParams, $"message.parts[{i}] must set exactly one of text, raw, url or data. Remove the extra fields.");
        }
        ValidateContextId(message.ContextId);
        ValidateHistoryLength(request.Configuration?.HistoryLength);

        var modes = card.DefaultInputModes;
        if (modes.Count == 0) return;
        foreach (var part in message.Parts)
        {
            var mediaType = part.EffectiveMediaType;
            if (!modes.Any(mode => MediaTypeMatches(mode, mediaType)))
                throw new A2AException(A2AErrorCode.ContentTypeNotSupported, $"Media type '{mediaType}' is not supported by this agent. Send a type from the card's defaultInputModes.");
        }
    }

    /// <summary>Returns requested ∩ declared extensions; throws when a required extension was not requested.</summary>
    public static IReadOnlyList<string> ResolveExtensions(AgentCard card, IReadOnlyList<string> requested)
    {
        var declared = card.Capabilities.Extensions;
        var missing = declared.Where(e => e.Required && e.Uri is { Length: > 0 } && !requested.Contains(e.Uri)).Select(e => e.Uri).ToList();
        if (missing.Count > 0)
            throw new A2AException(A2AErrorCode.ExtensionSupportRequired, $"Required extension(s) not requested: {string.Join(", ", missing)}. Send them in the A2A-Extensions header.");

        return ActiveExtensions(card, requested);
    }

    /// <summary>Requested extensions that the card declares, in request order, without duplicates.</summary>
    public static IReadOnlyList<string> ActiveExtensions(AgentCard card, IReadOnlyList<string> requested)
    {
        var declared = card.Capabilities.Extensions.Select(e => e.Uri).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return requested.Where(declared.Contains).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Matches a media type against a mode: exact, <c>type/*</c> or <c>*/*</c>. Case and parameters are ignored.</summary>
    public static bool MediaTypeMatches(string mode, string mediaType)
    {
        var m = Essence(mode);
        var t = Essence(mediaType);
        if (m == "*/*" || m == t) return true;
        return m.EndsWith("/*", StringComparison.Ordinal)
               && t.StartsWith(m[..^1], StringComparison.Ordinal);
    }

    private static string Essence(string mediaType)
    {
        var semicolon = mediaType.IndexOf(';');
        return (semicolon >= 0 ? mediaType[..semicolon] : mediaType).Trim().ToLowerInvariant();
    }

    /// <summary>Applies historyLength: null → all, 0 → none, n → last n.</summary>
    public static AgentTask ShapeHistory(AgentTask task, int? historyLength) => historyLength switch
    {
        null => task,
        0 => task with { History = [] },
        var n => task with { History = task.History.Skip(Math.Max(0, task.History.Count - n.Value)).ToList() },
    };
}
