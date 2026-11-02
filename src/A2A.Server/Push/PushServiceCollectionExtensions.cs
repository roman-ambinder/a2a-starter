using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace A2A.Server.Push;

internal static class PushServiceCollectionExtensions
{
    /// <summary>Registers the config store, signing key, sender (as observer and hosted service) and the <c>"a2a-push"</c> HTTP client.</summary>
    internal static IServiceCollection AddA2APush(this IServiceCollection services, Action<PushOptions>? configure = null)
    {
        services.AddOptions<PushOptions>();
        if (configure is not null) services.Configure(configure);
        services.TryAddSingleton<IPushConfigStore, InMemoryPushConfigStore>();
        services.TryAddSingleton<PushSigningKey>();
        services.TryAddSingleton<PushNotificationSender>();
        services.AddSingleton<Tasks.ITaskEventObserver>(sp => sp.GetRequiredService<PushNotificationSender>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PushNotificationSender>());
        services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient(c => c.Timeout = System.Threading.Timeout.InfiniteTimeSpan); // the sender applies PushOptions.Timeout per attempt
        return services;
    }
}
