using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Ambinder.A2A.Server.Push;

internal static class PushServiceCollectionExtensions
{
    /// <summary>Registers the config store, signing key, sender (as observer and hosted service) and the <c>"a2a-push"</c> HTTP client.</summary>
    internal static IServiceCollection AddA2APush(this IServiceCollection services, Action<PushOptions>? configure = null)
    {
        services.AddOptions<PushOptions>();
        if (configure is not null) services.Configure(configure);
        services.TryAddSingleton<IPushConfigStore, InMemoryPushConfigStore>();
        // TryAddEnumerable, so package 15 can add a formatter for another protocol version.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPushPayloadFormatter, DefaultPushPayloadFormatter>());
        services.TryAddSingleton<IPushCredentialProtector, PassThroughPushCredentialProtector>();
        services.TryAddSingleton<IPushRequestAuthenticator, NoOpPushRequestAuthenticator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PushCredentialProtectorStartupCheck>());
        services.TryAddSingleton<PushSigningKey>();
        services.TryAddKeyedSingleton<Func<System.Text.Json.Nodes.JsonObject>>(InternalServiceKeys.PushJwks,
            (sp, _) => () => sp.GetRequiredService<PushSigningKey>().GetJwks());
        services.TryAddSingleton<PushNotificationSender>();
        services.AddSingleton<Tasks.ITaskEventObserver>(sp => sp.GetRequiredService<PushNotificationSender>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PushNotificationSender>());
        services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                // Checked again at connect time, so DNS rebinding after config creation cannot reach private addresses.
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PushOptions>>().Value;
                var policy = new PushAddressPolicy(options);
                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    // A proxy would make the connect-time address check see the proxy, not the webhook host.
                    UseProxy = false,
                    ConnectCallback = (context, ct) => WebhookUrlValidator.ConnectCheckedAsync(policy, context.DnsEndPoint, ct),
                };
            })
            .ConfigureHttpClient(c => c.Timeout = System.Threading.Timeout.InfiniteTimeSpan); // the sender applies PushOptions.Timeout per attempt
        return services;
    }
}
