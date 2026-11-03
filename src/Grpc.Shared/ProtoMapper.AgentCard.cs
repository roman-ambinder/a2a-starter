using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Protocol.ProtoMapping;

static partial class ProtoMapper
{
    public static Proto.AgentInterface ToProto(AgentInterface x) => new()
    {
        Url = x.Url, ProtocolBinding = x.ProtocolBinding, Tenant = S(x.Tenant), ProtocolVersion = x.ProtocolVersion,
    };

    public static AgentInterface FromProto(Proto.AgentInterface x) => new()
    {
        Url = x.Url, ProtocolBinding = x.ProtocolBinding, Tenant = N(x.Tenant), ProtocolVersion = x.ProtocolVersion,
    };

    public static Proto.AgentProvider ToProto(AgentProvider x) => new() { Url = x.Url, Organization = x.Organization };

    public static AgentProvider FromProto(Proto.AgentProvider x) => new() { Url = x.Url, Organization = x.Organization };

    public static Proto.AgentExtension ToProto(AgentExtension x) => new()
    {
        Uri = S(x.Uri), Description = S(x.Description), Required = x.Required, Params = ToStruct(x.Params),
    };

    public static AgentExtension FromProto(Proto.AgentExtension x) => new()
    {
        Uri = N(x.Uri), Description = N(x.Description), Required = x.Required, Params = FromStruct(x.Params),
    };

    public static Proto.AgentCapabilities ToProto(AgentCapabilities x)
    {
        var c = new Proto.AgentCapabilities();
        if (x.Streaming is { } s) c.Streaming = s;
        if (x.PushNotifications is { } p) c.PushNotifications = p;
        if (x.ExtendedAgentCard is { } e) c.ExtendedAgentCard = e;
        c.Extensions.AddRange(x.Extensions.Select(ToProto));
        return c;
    }

    public static AgentCapabilities FromProto(Proto.AgentCapabilities x) => new()
    {
        Streaming = x.HasStreaming ? x.Streaming : null,
        PushNotifications = x.HasPushNotifications ? x.PushNotifications : null,
        ExtendedAgentCard = x.HasExtendedAgentCard ? x.ExtendedAgentCard : null,
        Extensions = L(x.Extensions.Select(FromProto)),
    };

    public static Proto.AgentSkill ToProto(AgentSkill x)
    {
        var s = new Proto.AgentSkill { Id = x.Id, Name = x.Name, Description = x.Description };
        s.Tags.AddRange(x.Tags);
        s.Examples.AddRange(x.Examples);
        s.InputModes.AddRange(x.InputModes);
        s.OutputModes.AddRange(x.OutputModes);
        s.SecurityRequirements.AddRange(x.SecurityRequirements.Select(ToProto));
        return s;
    }

    public static AgentSkill FromProto(Proto.AgentSkill x) => new()
    {
        Id = x.Id, Name = x.Name, Description = x.Description, Tags = L(x.Tags), Examples = L(x.Examples),
        InputModes = L(x.InputModes), OutputModes = L(x.OutputModes),
        SecurityRequirements = L(x.SecurityRequirements.Select(FromProto)),
    };

    public static Proto.AgentCardSignature ToProto(AgentCardSignature x) => new()
    {
        Protected = x.Protected, Signature = x.Signature, Header = ToStruct(x.Header),
    };

    public static AgentCardSignature FromProto(Proto.AgentCardSignature x) => new()
    {
        Protected = x.Protected, Signature = x.Signature, Header = FromStruct(x.Header),
    };

    public static Proto.AgentCard ToProto(AgentCard x)
    {
        var c = new Proto.AgentCard
        {
            Name = x.Name, Description = x.Description, Version = x.Version,
            Provider = x.Provider is null ? null : ToProto(x.Provider), Capabilities = ToProto(x.Capabilities),
        };
        if (x.DocumentationUrl is not null) c.DocumentationUrl = x.DocumentationUrl;
        if (x.IconUrl is not null) c.IconUrl = x.IconUrl;
        c.SupportedInterfaces.AddRange(x.SupportedInterfaces.Select(ToProto));
        if (x.SecuritySchemes is not null)
            foreach (var (key, value) in x.SecuritySchemes) c.SecuritySchemes[key] = ToProto(value);
        c.SecurityRequirements.AddRange(x.SecurityRequirements.Select(ToProto));
        c.DefaultInputModes.AddRange(x.DefaultInputModes);
        c.DefaultOutputModes.AddRange(x.DefaultOutputModes);
        c.Skills.AddRange(x.Skills.Select(ToProto));
        c.Signatures.AddRange(x.Signatures.Select(ToProto));
        return c;
    }

    public static AgentCard FromProto(Proto.AgentCard x) => new()
    {
        Name = x.Name, Description = x.Description, Version = x.Version,
        SupportedInterfaces = L(x.SupportedInterfaces.Select(FromProto)),
        Provider = x.Provider is null ? null : FromProto(x.Provider),
        DocumentationUrl = x.HasDocumentationUrl ? x.DocumentationUrl : null,
        Capabilities = x.Capabilities is null ? new() : FromProto(x.Capabilities),
        SecuritySchemes = x.SecuritySchemes.Count == 0 ? null : x.SecuritySchemes.ToDictionary(kv => kv.Key, kv => FromProto(kv.Value)),
        SecurityRequirements = L(x.SecurityRequirements.Select(FromProto)),
        DefaultInputModes = L(x.DefaultInputModes), DefaultOutputModes = L(x.DefaultOutputModes),
        Skills = L(x.Skills.Select(FromProto)), Signatures = L(x.Signatures.Select(FromProto)),
        IconUrl = x.HasIconUrl ? x.IconUrl : null,
    };
}
