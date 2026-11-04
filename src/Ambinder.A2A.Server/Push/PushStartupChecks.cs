using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Push;

/// <summary>Checks at host start that the push settings are safe for the environment. Fails the start with a message that names the fix.</summary>
internal sealed class PushStartupChecks : IHostedService
{
    internal const string ProxyNotEnforcedMessage =
        "A2A:Push:Proxy is set, but the proxy is not declared to block private networks. Set A2A:Push:ProxyEnforcesEgressPolicy=true once the proxy enforces egress rules, or remove the proxy.";

    private readonly PushOptions _options;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger _logger;

    public PushStartupChecks(IOptions<PushOptions> options, IHostEnvironment? environment = null, ILogger<PushStartupChecks>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _environment = environment;
        _logger = logger ?? NullLogger<PushStartupChecks>.Instance;
    }

    /// <summary>The proxy rule: a proxy outside Development needs <see cref="PushOptions.ProxyEnforcesEgressPolicy"/>. Returns the failure message, or <see langword="null"/>.</summary>
    internal static string? CheckProxy(PushOptions options, bool isDevelopment)
        => PushDeliveryMode.IsProxy(options) && !isDevelopment && !options.ProxyEnforcesEgressPolicy ? ProxyNotEnforcedMessage : null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (CheckProxy(_options, _environment?.IsDevelopment() == true) is { } failure)
            throw new InvalidOperationException(failure);
        if (PushDeliveryMode.IsProxy(_options))
        {
            var proxy = PushDeliveryMode.Describe(_options); // scheme://host:port only: the URI may hold credentials
            Log.PushProxyEnabled(_logger, proxy);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
