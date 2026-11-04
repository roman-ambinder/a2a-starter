using Ambinder.A2A.AspNetCore.Hosting;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Agents;

var builder = WebApplication.CreateBuilder(args);
var baseUrl = builder.Configuration["A2A:PublicBaseUrl"] ?? "http://localhost:5100";
builder.ConfigureA2AKestrel(); // listens on the port of PublicBaseUrl
builder.Services.AddA2AServer(o => { o.PublicBaseUrl = baseUrl; o.DefaultAgent = "hello"; })
    .AddAgent<HelloAgent>("hello", _ => new AgentCard { Name = "hello", Description = "Says hello.", Version = "1.0.0" });

var app = builder.Build();
app.MapA2A(); // the agent card and the JSON-RPC and HTTP+JSON endpoints
app.Run();

sealed class HelloAgent : IAgentHandler
{
    public ValueTask ExecuteAsync(AgentRequestContext context, IAgentEventSink sink, CancellationToken ct)
    {
        var name = context.Message.Parts.Select(p => p.Text).FirstOrDefault(t => t is not null);
        var reply = new Message { MessageId = Guid.NewGuid().ToString("N"), Role = Role.Agent, Parts = [Part.FromText($"Hello, {name}!")] };
        return sink.ReplyAsync(reply, ct);
    }
}
