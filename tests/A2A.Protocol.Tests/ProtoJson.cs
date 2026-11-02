using Google.Protobuf;
using Google.Protobuf.Reflection;
using Proto = Lf.A2a.V1;

namespace A2A.Protocol.Tests;

/// <summary>Parses JSON with the official proto parser, rejecting unknown fields.</summary>
public static class ProtoJson
{
    private static readonly JsonParser Parser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(false));

    private static readonly Dictionary<string, string> Renames = new()
    {
        ["AgentTask"] = "Task",
        ["AgentTaskStatus"] = "TaskStatus",
        ["ApiKeySecurityScheme"] = "APIKeySecurityScheme",
        ["HttpAuthSecurityScheme"] = "HTTPAuthSecurityScheme",
    };

    public static IMessage Parse(Type ourModelType, string json)
    {
        var name = Renames.GetValueOrDefault(ourModelType.Name, ourModelType.Name);
        var generated = typeof(Proto.Task).Assembly.GetType($"Lf.A2a.V1.{name}")
            ?? throw new InvalidOperationException($"No generated type for {ourModelType.Name}");
        var descriptor = (MessageDescriptor)generated.GetProperty("Descriptor")!.GetValue(null)!;
        return Parser.Parse(json, descriptor);
    }
}
