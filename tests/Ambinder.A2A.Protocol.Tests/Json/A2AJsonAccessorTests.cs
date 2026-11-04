using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Ambinder.A2A.Protocol.Tests.Json;

public class A2AJsonAccessorTests
{
    // Error and constant types are not wire models, so they get no accessor.
    private static readonly HashSet<string> NotModels =
    [
        nameof(A2AException), nameof(A2AAuthException), nameof(A2AAuthFailureKind), nameof(A2AErrorCode), nameof(A2AErrorInfo),
        nameof(A2AError), nameof(A2AConstants), nameof(A2ATelemetryNames),
        // Public only so the source generator accepts [JsonConverter] on TaskState and Role (owner decision 2026-11-04).
        "ProtoEnumConverter`1",
    ];

    private static IEnumerable<Type> ModelTypes() => typeof(A2AJson).Assembly.GetExportedTypes()
        .Where(t => t.Namespace == "Ambinder.A2A.Protocol" && !NotModels.Contains(t.Name))
        .Where(t => t.IsEnum || (t.IsClass && t.IsSealed && !t.IsAbstract));

    private static IEnumerable<PropertyInfo> Accessors() => typeof(A2AJson)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(JsonTypeInfo<>));

    [Fact]
    public void Every_public_protocol_type_has_a_typed_accessor()
    {
        var models = ModelTypes().ToList();
        Assert.NotEmpty(models);
        var accessorTypes = Accessors().Select(p => p.PropertyType.GetGenericArguments()[0]).ToHashSet();
        var missing = models.Where(t => !accessorTypes.Contains(t)).Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0, "No A2AJson accessor for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Accessors_use_the_shared_options()
    {
        foreach (var property in Accessors())
        {
            var info = (JsonTypeInfo)property.GetValue(null)!;
            Assert.Same(A2AJson.Options, info.Options);
        }
    }

    [Fact]
    public void TypeInfoResolver_resolves_protocol_types()
        => Assert.NotNull(A2AJson.TypeInfoResolver.GetTypeInfo(typeof(AgentTask), new JsonSerializerOptions()));

    [Fact]
    public void Card_read_is_unchanged()
    {
        var json = JsonSerializer.Serialize(Samples.FullCard(), A2AJson.Options);
        var typed = JsonSerializer.Deserialize(json, A2AJson.AgentCard)!;
        var viaOptions = (AgentCard)JsonSerializer.Deserialize(json, typeof(AgentCard), A2AJson.Options)!;
        Assert.Equal(JsonSerializer.Serialize(viaOptions, A2AJson.Options), JsonSerializer.Serialize(typed, A2AJson.Options));
        Assert.Equal(json, JsonSerializer.Serialize(typed, A2AJson.Options));
    }
}
