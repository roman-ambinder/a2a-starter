using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Server.Tasks;

/// <summary>The request rules of ListTasks that do not depend on the stored data.</summary>
internal static class ListTasksRules
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>The page size to use for <paramref name="request"/>: its own, or the default.</summary>
    /// <exception cref="A2AException"><c>InvalidParams</c>: the page size or history length is out of range.</exception>
    public static int ValidatePageSizeAndHistory(ListTasksRequest request)
    {
        var pageSize = request.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
            throw new A2AException(A2AErrorCode.InvalidParams, $"pageSize must be between 1 and {MaxPageSize}. Send a value in that range, or omit it.");
        if (request.HistoryLength is < 0)
            throw new A2AException(A2AErrorCode.InvalidParams, "historyLength must not be negative. Send 0 or more, or omit it.");
        return pageSize;
    }
}
