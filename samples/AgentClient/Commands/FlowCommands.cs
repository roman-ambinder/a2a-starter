using System.CommandLine;

namespace AgentClient.Commands;

/// <summary>Builds the System.CommandLine tree: one command per flow, plus <c>all</c>.</summary>
public static class FlowCommands
{
    /// <summary>Command name, description.</summary>
    private static readonly (string Name, string Description)[] Flows =
    [
        ("discover", "Fetch the agent cards of all four agents"),
        ("send", "Blocking send to the report agent"),
        ("stream", "Streaming send to the report agent"),
        ("get", "Send without waiting, then poll GetTask"),
        ("list", "List, filter and page tasks"),
        ("cancel", "Cancel a running report"),
        ("resubscribe", "Drop a stream, then attach again with SubscribeToTask"),
        ("multi-turn", "Booking agent: several turns on one task"),
        ("push", "Push notifications to the WebhookReceiver"),
        ("auth", "Secure agent: auth-required step-up, 401 and 403"),
        ("extended-card", "Authenticated extended agent card"),
        ("extensions", "Timestamp and audit extensions"),
        ("errors", "Every protocol error the demo servers produce"),
    ];

    public static RootCommand Build(TextWriter output, Func<FlowOptions, FlowOptions>? configure = null)
    {
        var defaults = FlowOptions.FromSettings();
        var server = new Option<string>("--server") { Description = "Base URL of the AgentServer", Recursive = true, DefaultValueFactory = _ => defaults.ServerUrl };
        var agent = new Option<string?>("--agent") { Description = "Agent to talk to (default depends on the command)", Recursive = true };
        var transport = new Option<string>("--transport") { Description = "jsonrpc | rest | grpc | auto", Recursive = true, DefaultValueFactory = _ => "auto" };
        var auth = new Option<string>("--auth") { Description = "none | apikey | mock | entra", Recursive = true, DefaultValueFactory = _ => "mock" };
        var scope = new Option<string?>("--scope") { Description = "OAuth scope for --auth entra (default: the demo scope)", Recursive = true };
        var verbose = new Option<bool>("--verbose") { Description = "Print every request and response (secrets are masked)", Recursive = true };
        var receiver = new Option<string>("--receiver") { Description = "Base URL of the WebhookReceiver (push flow)", Recursive = true, DefaultValueFactory = _ => defaults.ReceiverUrl };

        var root = new RootCommand("A2A console client: runs every demo flow against the AgentServer.");
        foreach (var o in new Option[] { server, agent, transport, auth, scope, verbose, receiver }) root.Options.Add(o);

        FlowOptions OptionsFrom(ParseResult parse)
        {
            var options = defaults with
            {
                ServerUrl = parse.GetValue(server)!,
                Agent = parse.GetValue(agent),
                Binding = FlowOptions.ParseBinding(parse.GetValue(transport)),
                Auth = FlowOptions.ParseAuth(parse.GetValue(auth)),
                Scope = parse.GetValue(scope) ?? defaults.Scope,
                Verbose = parse.GetValue(verbose),
                ReceiverUrl = parse.GetValue(receiver)!,
                Output = output,
            };
            return configure?.Invoke(options) ?? options;
        }

        foreach (var (name, description) in Flows)
        {
            var command = new Command(name, description);
            command.SetAction((parse, ct) => RunAsync(parse, OptionsFrom, output, async runner => [await runner.RunAsync(name, ct)], ct));
            root.Subcommands.Add(command);
        }

        var all = new Command("all", "Run every flow");
        all.SetAction((parse, ct) => RunAsync(parse, OptionsFrom, output, runner => runner.RunAllAsync(ct), ct));
        root.Subcommands.Add(all);
        return root;
    }

    private static async Task<int> RunAsync(ParseResult parse, Func<ParseResult, FlowOptions> optionsFrom, TextWriter output,
        Func<FlowRunner, Task<IReadOnlyList<FlowResult>>> run, CancellationToken ct)
    {
        FlowOptions options;
        try
        {
            options = optionsFrom(parse);
        }
        catch (ArgumentException e)
        {
            await Console.Error.WriteLineAsync(e.Message);
            return 2;
        }

        using var runner = new FlowRunner(options);
        var results = await run(runner);
        output.WriteLine();
        output.WriteLine("Summary");
        foreach (var r in results) output.WriteLine($"  {(r.Passed ? "PASS" : "FAIL")}  {r.Flow}");
        var failed = results.Count(r => !r.Passed);
        output.WriteLine(failed == 0 ? $"All {results.Count} flow(s) passed." : $"{failed} of {results.Count} flow(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
