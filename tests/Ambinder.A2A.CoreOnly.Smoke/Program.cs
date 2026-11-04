// Uses the core packages (Server, Client) without ASP.NET Core, gRPC or Azure.
// Run by eng/check-core-only.sh on the published output; not part of any test run.
using Ambinder.A2A.Client;
using Ambinder.A2A.Server;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddA2AServerCore(o => o.PublicBaseUrl = "http://localhost:5000");
using var provider = services.BuildServiceProvider();

var handler = provider.GetRequiredService<IA2ARequestHandler>();
var options = new A2AClientOptions();
Console.WriteLine($"Server core: {handler.GetType().Name}; client: {typeof(A2AClientFactory).Name}, {options.GetType().Name}");
