using System.CommandLine;
using AgentClient.Commands;

return await FlowCommands.Build(Console.Out).Parse(args).InvokeAsync();
