using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ambinder.A2A.Server.Push;

/// <summary>Logs one warning at startup when push credentials are stored unprotected outside Development.</summary>
internal sealed class PushCredentialProtectorStartupCheck : IHostedService
{
    private readonly IPushCredentialProtector _protector;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger _logger;

    public PushCredentialProtectorStartupCheck(IPushCredentialProtector protector, IHostEnvironment? environment = null, ILogger<PushCredentialProtectorStartupCheck>? logger = null)
    {
        _protector = protector;
        _environment = environment;
        _logger = logger ?? NullLogger<PushCredentialProtectorStartupCheck>.Instance;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_protector is PassThroughPushCredentialProtector && _environment?.IsDevelopment() != true)
        {
            Log.PushCredentialsUnprotected(_logger);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
