using A2A.Protocol;

namespace A2A.Server.Tasks;

/// <summary>Checks an incoming message against the protocol rules and the agent card.</summary>
internal static class RequestValidator
{
    public static void ValidateHistoryLength(int? historyLength)
    {
        if (historyLength is < 0)
            throw new A2AException(A2AErrorCode.InvalidParams, "historyLength must not be negative.");
    }

    public static void ValidateMessage(SendMessageRequest request, AgentCard card)
    {
        var message = request.Message;
        if (string.IsNullOrEmpty(message.MessageId))
            throw new A2AException(A2AErrorCode.InvalidParams, "message.messageId is required.");
        if (message.Role != Role.User)
            throw new A2AException(A2AErrorCode.InvalidParams, "message.role must be ROLE_USER.");
        if (message.Parts.Count == 0)
            throw new A2AException(A2AErrorCode.InvalidParams, "message.parts must not be empty.");
        for (var i = 0; i < message.Parts.Count; i++)
        {
            if (!message.Parts[i].IsValid)
                throw new A2AException(A2AErrorCode.InvalidParams, $"message.parts[{i}] must set exactly one of text, raw, url or data.");
        }
        ValidateHistoryLength(request.Configuration?.HistoryLength);

        var modes = card.DefaultInputModes;
        if (modes.Count == 0) return;
        foreach (var part in message.Parts)
        {
            var mediaType = part.EffectiveMediaType;
            if (!modes.Any(mode => MediaTypeMatches(mode, mediaType)))
                throw new A2AException(A2AErrorCode.ContentTypeNotSupported, $"Media type '{mediaType}' is not supported by this agent.");
        }
    }

    /// <summary>Returns requested ∩ declared extensions; throws when a required extension was not requested.</summary>
    public static IReadOnlyList<string> ResolveExtensions(AgentCard card, IReadOnlyList<string> requested)
    {
        var declared = card.Capabilities.Extensions;
        var missing = declared.Where(e => e.Required && e.Uri is { Length: > 0 } && !requested.Contains(e.Uri)).Select(e => e.Uri).ToList();
        if (missing.Count > 0)
            throw new A2AException(A2AErrorCode.ExtensionSupportRequired, $"Required extension(s) not requested: {string.Join(", ", missing)}.");

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
