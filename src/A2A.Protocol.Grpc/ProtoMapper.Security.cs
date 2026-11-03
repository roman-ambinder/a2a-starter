using Proto = Lf.A2a.V1;

namespace A2A.Protocol.Grpc;

public static partial class ProtoMapper
{
    public static Proto.StringList ToProto(StringList x)
    {
        var l = new Proto.StringList();
        l.List.AddRange(x.List);
        return l;
    }

    public static StringList FromProto(Proto.StringList x) => new() { List = L(x.List) };

    public static Proto.SecurityRequirement ToProto(SecurityRequirement x)
    {
        var r = new Proto.SecurityRequirement();
        foreach (var (key, value) in x.Schemes) r.Schemes[key] = ToProto(value);
        return r;
    }

    public static SecurityRequirement FromProto(Proto.SecurityRequirement x)
        => new() { Schemes = x.Schemes.ToDictionary(kv => kv.Key, kv => FromProto(kv.Value)) };

    public static Proto.SecurityScheme ToProto(SecurityScheme x)
    {
        var s = new Proto.SecurityScheme();
        if (x.ApiKeySecurityScheme is { } a)
            s.ApiKeySecurityScheme = new Proto.APIKeySecurityScheme { Description = S(a.Description), Location = a.Location, Name = a.Name };
        else if (x.HttpAuthSecurityScheme is { } h)
            s.HttpAuthSecurityScheme = new Proto.HTTPAuthSecurityScheme { Description = S(h.Description), Scheme = h.Scheme, BearerFormat = S(h.BearerFormat) };
        else if (x.Oauth2SecurityScheme is { } o)
            s.Oauth2SecurityScheme = new Proto.OAuth2SecurityScheme { Description = S(o.Description), Flows = ToProto(o.Flows), Oauth2MetadataUrl = S(o.Oauth2MetadataUrl) };
        else if (x.OpenIdConnectSecurityScheme is { } i)
            s.OpenIdConnectSecurityScheme = new Proto.OpenIdConnectSecurityScheme { Description = S(i.Description), OpenIdConnectUrl = i.OpenIdConnectUrl };
        else if (x.MtlsSecurityScheme is { } m)
            s.MtlsSecurityScheme = new Proto.MutualTlsSecurityScheme { Description = S(m.Description) };
        return s;
    }

    public static SecurityScheme FromProto(Proto.SecurityScheme x) => x.SchemeCase switch
    {
        Proto.SecurityScheme.SchemeOneofCase.ApiKeySecurityScheme => new()
        {
            ApiKeySecurityScheme = new ApiKeySecurityScheme
            {
                Description = N(x.ApiKeySecurityScheme.Description), Location = x.ApiKeySecurityScheme.Location, Name = x.ApiKeySecurityScheme.Name,
            },
        },
        Proto.SecurityScheme.SchemeOneofCase.HttpAuthSecurityScheme => new()
        {
            HttpAuthSecurityScheme = new HttpAuthSecurityScheme
            {
                Description = N(x.HttpAuthSecurityScheme.Description), Scheme = x.HttpAuthSecurityScheme.Scheme,
                BearerFormat = N(x.HttpAuthSecurityScheme.BearerFormat),
            },
        },
        Proto.SecurityScheme.SchemeOneofCase.Oauth2SecurityScheme => new()
        {
            Oauth2SecurityScheme = new OAuth2SecurityScheme
            {
                Description = N(x.Oauth2SecurityScheme.Description), Flows = FromProto(x.Oauth2SecurityScheme.Flows),
                Oauth2MetadataUrl = N(x.Oauth2SecurityScheme.Oauth2MetadataUrl),
            },
        },
        Proto.SecurityScheme.SchemeOneofCase.OpenIdConnectSecurityScheme => new()
        {
            OpenIdConnectSecurityScheme = new OpenIdConnectSecurityScheme
            {
                Description = N(x.OpenIdConnectSecurityScheme.Description), OpenIdConnectUrl = x.OpenIdConnectSecurityScheme.OpenIdConnectUrl,
            },
        },
        Proto.SecurityScheme.SchemeOneofCase.MtlsSecurityScheme => new()
        {
            MtlsSecurityScheme = new MutualTlsSecurityScheme { Description = N(x.MtlsSecurityScheme.Description) },
        },
        _ => new(),
    };

    public static Proto.OAuthFlows ToProto(OAuthFlows x)
    {
        var f = new Proto.OAuthFlows();
        if (x.AuthorizationCode is { } a)
        {
            var flow = new Proto.AuthorizationCodeOAuthFlow
            {
                AuthorizationUrl = a.AuthorizationUrl, TokenUrl = a.TokenUrl, RefreshUrl = S(a.RefreshUrl), PkceRequired = a.PkceRequired,
            };
            flow.Scopes.Add(ToDict(a.Scopes));
            f.AuthorizationCode = flow;
        }
        else if (x.ClientCredentials is { } c)
        {
            var flow = new Proto.ClientCredentialsOAuthFlow { TokenUrl = c.TokenUrl, RefreshUrl = S(c.RefreshUrl) };
            flow.Scopes.Add(ToDict(c.Scopes));
            f.ClientCredentials = flow;
        }
        else if (x.DeviceCode is { } d)
        {
            var flow = new Proto.DeviceCodeOAuthFlow
            {
                DeviceAuthorizationUrl = d.DeviceAuthorizationUrl, TokenUrl = d.TokenUrl, RefreshUrl = S(d.RefreshUrl),
            };
            flow.Scopes.Add(ToDict(d.Scopes));
            f.DeviceCode = flow;
        }
        return f;
    }

    /// <summary>Deprecated implicit and password flows are not modeled and map to an empty value.</summary>
    public static OAuthFlows FromProto(Proto.OAuthFlows x) => x.FlowCase switch
    {
        Proto.OAuthFlows.FlowOneofCase.AuthorizationCode => new()
        {
            AuthorizationCode = new AuthorizationCodeOAuthFlow
            {
                AuthorizationUrl = x.AuthorizationCode.AuthorizationUrl, TokenUrl = x.AuthorizationCode.TokenUrl,
                RefreshUrl = N(x.AuthorizationCode.RefreshUrl), Scopes = new Dictionary<string, string>(x.AuthorizationCode.Scopes),
                PkceRequired = x.AuthorizationCode.PkceRequired,
            },
        },
        Proto.OAuthFlows.FlowOneofCase.ClientCredentials => new()
        {
            ClientCredentials = new ClientCredentialsOAuthFlow
            {
                TokenUrl = x.ClientCredentials.TokenUrl, RefreshUrl = N(x.ClientCredentials.RefreshUrl),
                Scopes = new Dictionary<string, string>(x.ClientCredentials.Scopes),
            },
        },
        Proto.OAuthFlows.FlowOneofCase.DeviceCode => new()
        {
            DeviceCode = new DeviceCodeOAuthFlow
            {
                DeviceAuthorizationUrl = x.DeviceCode.DeviceAuthorizationUrl, TokenUrl = x.DeviceCode.TokenUrl,
                RefreshUrl = N(x.DeviceCode.RefreshUrl), Scopes = new Dictionary<string, string>(x.DeviceCode.Scopes),
            },
        },
        _ => new(),
    };
}
