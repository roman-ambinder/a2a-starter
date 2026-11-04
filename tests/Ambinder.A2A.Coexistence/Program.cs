// Compile-only check: the Ambinder.A2A packages and the official A2A package can be referenced by the same project.
// Both define AgentCard; the official one lives in namespace A2A, ours in Ambinder.A2A.Protocol.
// This project is built with the solution but never run in CI.
using Ambinder.A2A.Client;
using Ambinder.A2A.Server;
using OfficialA2A = A2A;
using OurCard = Ambinder.A2A.Protocol.AgentCard;

var official = new OfficialA2A.AgentCard { Name = "official" };
var ours = new OurCard { Name = "ambinder" };
Console.WriteLine($"{official.GetType().FullName}: {official.Name}");
Console.WriteLine($"{ours.GetType().FullName}: {ours.Name}");

// One type from each of our other assemblies, so every project reference is used.
Console.WriteLine(typeof(A2AClientFactory).FullName);
Console.WriteLine(typeof(IA2ARequestHandler).FullName);
