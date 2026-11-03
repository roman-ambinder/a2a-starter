using System.CommandLine;
using AgentClient;
using AgentClient.Commands;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
// Disposed after the command ends, so spans and metrics are flushed before exit.
using var telemetry = ClientTelemetry.Start(config);
return await FlowCommands.Build(Console.Out).Parse(args).InvokeAsync();
