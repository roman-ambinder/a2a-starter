using Ambinder.A2A.Client;
using Ambinder.A2A.Protocol;

using var http = new HttpClient();
var card = await new A2ACardResolver(http).GetAgentCardAsync(new Uri(args.ElementAtOrDefault(0) ?? "http://localhost:5100"));
var client = A2AClientFactory.Create(card, new A2AClientOptions { HttpClient = http });
var reply = await client.SendMessageAsync(new SendMessageRequest
{
    Message = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.User, Parts = [Part.FromText(args.ElementAtOrDefault(1) ?? "hello")] },
});

// A reply is either a message or a task; a task carries its text in the status message or in an artifact.
var parts = reply.Message?.Parts ?? reply.Task?.Status.Message?.Parts ?? reply.Task?.Artifacts.FirstOrDefault()?.Parts ?? [];
var text = string.Join(" ", parts.Select(p => p.Text).Where(t => t is not null));
Console.WriteLine(text);
return text.Length > 0 ? 0 : 1;
