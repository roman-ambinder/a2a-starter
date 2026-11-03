using Proto = Lf.A2a.V1;

namespace Ambinder.A2A.Protocol.ProtoMapping;

static partial class ProtoMapper
{
    public static Proto.AuthenticationInfo ToProto(AuthenticationInfo x) => new() { Scheme = x.Scheme, Credentials = S(x.Credentials) };

    public static AuthenticationInfo FromProto(Proto.AuthenticationInfo x) => new() { Scheme = x.Scheme, Credentials = N(x.Credentials) };

    public static Proto.TaskPushNotificationConfig ToProto(TaskPushNotificationConfig x) => new()
    {
        Tenant = S(x.Tenant), Id = S(x.Id), TaskId = S(x.TaskId), Url = x.Url, Token = S(x.Token),
        Authentication = x.Authentication is null ? null : ToProto(x.Authentication),
    };

    public static TaskPushNotificationConfig FromProto(Proto.TaskPushNotificationConfig x) => new()
    {
        Tenant = N(x.Tenant), Id = N(x.Id), TaskId = N(x.TaskId), Url = x.Url, Token = N(x.Token),
        Authentication = x.Authentication is null ? null : FromProto(x.Authentication),
    };
}
