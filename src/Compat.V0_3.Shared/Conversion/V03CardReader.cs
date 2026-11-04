using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ambinder.A2A.Compat.V0_3.Shared.Json;
using Ambinder.A2A.Compat.V0_3.Shared.Models;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Compat.V0_3.Shared.Conversion;

/// <summary>
/// Reads a 0.3-only agent card (it has <c>url</c> and no <c>supportedInterfaces</c>) as a 1.0 <see cref="AgentCard"/>.
/// What has no 1.0 home is listed in docs/spec-decisions.md, "Cards".
/// </summary>
internal static class V03CardReader
{
    /// <summary>
    /// Converts card JSON. Returns <see langword="false"/> (not handled) when the JSON is not an object, has
    /// <c>supportedInterfaces</c> (a 1.0 card, also a dual card), or has no <c>url</c>.
    /// </summary>
    /// <exception cref="JsonException">The card claims to be 0.3 but a member has the wrong type.</exception>
    public static bool TryRead(JsonElement json, [NotNullWhen(true)] out AgentCard? card)
    {
        card = null;
        if (json.ValueKind != JsonValueKind.Object
            || json.TryGetProperty("supportedInterfaces", out _)
            || !json.TryGetProperty("url", out JsonElement url) || url.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        V03AgentCard v03 = json.Deserialize(V0_3JsonContext.Default.V03AgentCard) ?? throw new JsonException("Empty 0.3 agent card.");
        card = ToV1(v03);
        return true;
    }

    /// <summary>Converts a 0.3 card model to a 1.0 card.</summary>
    public static AgentCard ToV1(V03AgentCard card) => new()
    {
        Name = card.Name,
        Description = card.Description,
        Version = card.Version,
        DocumentationUrl = card.DocumentationUrl,
        IconUrl = card.IconUrl,
        SupportedInterfaces = ToInterfaces(card),
        Provider = card.Provider is { } p ? new AgentProvider { Organization = p.Organization, Url = p.Url } : null,
        Capabilities = ToCapabilities(card),
        SecuritySchemes = card.SecuritySchemes?.Select(kv => (kv.Key, Scheme: ToScheme(kv.Value))).Where(x => x.Scheme is not null)
            .ToDictionary(x => x.Key, x => x.Scheme!),
        SecurityRequirements = ToRequirements(card.Security),
        DefaultInputModes = card.DefaultInputModes?.ToArray() ?? [],
        DefaultOutputModes = card.DefaultOutputModes?.ToArray() ?? [],
        Skills = card.Skills?.Select(ToSkill).ToArray() ?? [],
        Signatures = card.Signatures?.Select(s => new AgentCardSignature
        {
            Protected = s.Protected,
            Signature = s.Signature,
            Header = V03MessageConverter.ToV1Metadata(s.Header),
        }).ToArray() ?? [],
    };

    private static List<AgentInterface> ToInterfaces(V03AgentCard card)
    {
        List<AgentInterface> result = [];
        Add(card.Url, string.IsNullOrEmpty(card.PreferredTransport) ? A2AConstants.BindingJsonRpc : card.PreferredTransport);
        foreach (V03AgentInterface i in card.AdditionalInterfaces ?? [])
        {
            Add(i.Url, string.IsNullOrEmpty(i.Transport) ? A2AConstants.BindingJsonRpc : i.Transport);
        }

        return result;

        void Add(string url, string binding)
        {
            if (!result.Exists(r => r.Url == url && r.ProtocolBinding == binding))
            {
                result.Add(new AgentInterface { Url = url, ProtocolBinding = binding, ProtocolVersion = V03CardWriter.InterfaceVersion });
            }
        }
    }

    private static AgentCapabilities ToCapabilities(V03AgentCard card) => new()
    {
        Streaming = card.Capabilities?.Streaming,
        PushNotifications = card.Capabilities?.PushNotifications,
        ExtendedAgentCard = card.SupportsAuthenticatedExtendedCard,
        Extensions = card.Capabilities?.Extensions?.Select(e => new AgentExtension
        {
            Uri = e.Uri,
            Description = e.Description,
            Required = e.Required ?? false,
            Params = V03MessageConverter.ToV1Metadata(e.Params),
        }).ToArray() ?? [],
    };

    private static AgentSkill ToSkill(V03AgentSkill s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Description = s.Description,
        Tags = s.Tags?.ToArray() ?? [],
        Examples = s.Examples?.ToArray() ?? [],
        InputModes = s.InputModes?.ToArray() ?? [],
        OutputModes = s.OutputModes?.ToArray() ?? [],
        SecurityRequirements = ToRequirements(s.Security),
    };

    private static SecurityRequirement[] ToRequirements(IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>? security) =>
        security?.Select(map => new SecurityRequirement
        {
            Schemes = map.ToDictionary(kv => kv.Key, kv => new StringList { List = kv.Value?.ToArray() ?? [] }),
        }).ToArray() ?? [];

    /// <summary>Converts a flat 0.3 scheme; an unknown <c>type</c> gives <see langword="null"/> (the scheme is dropped).</summary>
    private static SecurityScheme? ToScheme(V03SecurityScheme s) => s.Type switch
    {
        "apiKey" => new SecurityScheme { ApiKeySecurityScheme = new ApiKeySecurityScheme { Description = s.Description, Location = s.In ?? "", Name = s.Name ?? "" } },
        "http" => new SecurityScheme { HttpAuthSecurityScheme = new HttpAuthSecurityScheme { Description = s.Description, Scheme = s.Scheme ?? "", BearerFormat = s.BearerFormat } },
        "oauth2" => new SecurityScheme { Oauth2SecurityScheme = new OAuth2SecurityScheme { Description = s.Description, Flows = ToFlows(s.Flows), Oauth2MetadataUrl = s.Oauth2MetadataUrl } },
        "openIdConnect" => new SecurityScheme { OpenIdConnectSecurityScheme = new OpenIdConnectSecurityScheme { Description = s.Description, OpenIdConnectUrl = s.OpenIdConnectUrl ?? "" } },
        "mutualTLS" => new SecurityScheme { MtlsSecurityScheme = new MutualTlsSecurityScheme { Description = s.Description } },
        _ => null,
    };

#pragma warning disable CS0618 // implicit and password flows are deprecated in 1.0 but 0.3 cards use them
    /// <summary>
    /// 1.0 <c>OAuthFlows</c> is a oneof; 0.3 allows several flows. Implementer default (awaiting owner review): the first present flow in the order
    /// authorizationCode, clientCredentials, implicit, password is kept and the others are dropped.
    /// </summary>
    private static OAuthFlows ToFlows(V03OAuthFlows? f)
    {
        if (f?.AuthorizationCode is { } ac)
        {
            return new OAuthFlows { AuthorizationCode = new AuthorizationCodeOAuthFlow { AuthorizationUrl = ac.AuthorizationUrl ?? "", TokenUrl = ac.TokenUrl ?? "", RefreshUrl = ac.RefreshUrl, Scopes = Scopes(ac) } };
        }

        if (f?.ClientCredentials is { } cc)
        {
            return new OAuthFlows { ClientCredentials = new ClientCredentialsOAuthFlow { TokenUrl = cc.TokenUrl ?? "", RefreshUrl = cc.RefreshUrl, Scopes = Scopes(cc) } };
        }

        if (f?.Implicit is { } im)
        {
            return new OAuthFlows { Implicit = new ImplicitOAuthFlow { AuthorizationUrl = im.AuthorizationUrl ?? "", RefreshUrl = im.RefreshUrl, Scopes = Scopes(im) } };
        }

        if (f?.Password is { } pw)
        {
            return new OAuthFlows { Password = new PasswordOAuthFlow { TokenUrl = pw.TokenUrl ?? "", RefreshUrl = pw.RefreshUrl, Scopes = Scopes(pw) } };
        }

        return new OAuthFlows();
    }
#pragma warning restore CS0618

    private static Dictionary<string, string> Scopes(V03OAuthFlow flow) => flow.Scopes is null ? [] : new Dictionary<string, string>(flow.Scopes);
}
