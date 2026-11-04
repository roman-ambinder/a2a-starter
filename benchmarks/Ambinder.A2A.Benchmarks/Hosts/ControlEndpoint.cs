using System.Text.Json;
using Ambinder.A2A.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Ambinder.A2A.Benchmarks.Hosts;

/// <summary>
/// The control (spec 14 section 1): a hand-written minimal API that reads a <see cref="SendMessageRequest"/> and writes a canned
/// <see cref="SendMessageResponse"/>, with the same System.Text.Json source-generated metadata. It shows what the platform itself
/// costs, so the gate can report server-only <c>SendMessage</c> as a ratio to it.
/// </summary>
internal static class ControlEndpoint
{
    public const string Path = "/control";

    // The same reply HelloAgent gives.
    public static readonly SendMessageResponse Canned = new()
    {
        Message = new Message { MessageId = "hello-reply", Role = Role.Agent, Parts = [Part.FromText(BenchServer.HelloText)] },
    };

    public static void Map(WebApplication app)
        => app.MapPost(Path, async (HttpContext http) =>
        {
            var request = await JsonSerializer.DeserializeAsync(http.Request.Body, A2AJson.SendMessageRequest, http.RequestAborted).ConfigureAwait(false);
            if (request is null || request.Message.Parts.Count == 0)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            http.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(http.Response.Body, Canned, A2AJson.SendMessageResponse, http.RequestAborted).ConfigureAwait(false);
        });
}
