using System.Text.Json;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Hosting;

// System under test for the official A2A TCK (https://github.com/a2aproject/a2a-tck). Public SDK APIs only.
// Run:  dotnet run --project tests/A2A.Tck.Sut -- --port 9999 --grpc-port 10000
// Then: ./run_tck.py --sut-host http://localhost:9999   (from an a2a-tck checkout)
var port = Arg("--port", 9999);
var grpcPort = Arg("--grpc-port", port + 1);
var baseUrl = $"http://localhost:{port}";

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services
    .AddA2AServer(o =>
    {
        o.PublicBaseUrl = baseUrl;
        o.DefaultAgent = TckAgent.Name;
        o.GrpcPorts[TckAgent.Name] = grpcPort;
        o.Push.AllowPrivateNetworks = true; // the TCK webhook receiver listens on localhost
    })
    .AddAgent<TckAgent>(TckAgent.Name, _ => TckAgent.Card);
builder.ConfigureA2AKestrel();

var app = builder.Build();
app.MapA2A();
app.MapA2AGrpc();
app.Run();

int Arg(string name, int fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
}
