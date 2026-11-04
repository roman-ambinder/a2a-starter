using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Push;

/// <summary>Checks <see cref="PushOptions"/> at startup. Each message names the option and says what to change.</summary>
internal sealed class PushOptionsValidator : IValidateOptions<PushOptions>
{
    private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan MinTokenLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxTokenLifetime = TimeSpan.FromMinutes(10);

    public ValidateOptionsResult Validate(string? name, PushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];

        if (options.Timeout < MinTimeout || options.Timeout > MaxTimeout)
        {
            failures.Add($"A2A:Push:Timeout is {Show(options.Timeout)}, which is outside the allowed range of 1 to 120 seconds. Set it between 1 and 120 seconds; 10 to 30 seconds works for most receivers.");
        }
        else if (options.ClaimDuration <= options.Timeout * 2)
        {
            failures.Add($"A2A:Push:ClaimDuration is {Show(options.ClaimDuration)}, which is not more than twice A2A:Push:Timeout ({Show(options.Timeout)}). Raise ClaimDuration above {Show(options.Timeout * 2)} or lower Timeout, so a claim cannot expire during an attempt.");
        }

        if (options.MaxAttempts < 1)
        {
            failures.Add($"A2A:Push:MaxAttempts is {options.MaxAttempts.ToString(CultureInfo.InvariantCulture)}, which is below 1. Set it to the most attempts per delivery, including the first, for example 10.");
        }
        if (options.BatchSize < 1)
        {
            failures.Add($"A2A:Push:BatchSize is {options.BatchSize.ToString(CultureInfo.InvariantCulture)}, which is not positive. Set it to the number of deliveries to claim per call, for example 64.");
        }
        if (options.MaxConcurrentDeliveries < 1)
        {
            failures.Add($"A2A:Push:MaxConcurrentDeliveries is {options.MaxConcurrentDeliveries.ToString(CultureInfo.InvariantCulture)}, which is not positive. Set it to the number of deliveries to run at once, for example 32.");
        }
        if (options.MaxBackoff < options.InitialBackoff)
        {
            failures.Add($"A2A:Push:MaxBackoff ({Show(options.MaxBackoff)}) is below A2A:Push:InitialBackoff ({Show(options.InitialBackoff)}). Set MaxBackoff to at least InitialBackoff.");
        }

        for (var i = 0; i < options.AllowedNetworks.Count; i++)
        {
            var cidr = options.AllowedNetworks[i];
            if (!IsValidCidr(cidr))
            {
                failures.Add($"A2A:Push:AllowedNetworks:{i.ToString(CultureInfo.InvariantCulture)} is '{cidr}', which is not a CIDR range. Use an address and a prefix length, for example 10.1.0.0/16 or fd00::/8.");
            }
        }

        if (options.Proxy is { IsAbsoluteUri: false })
        {
            failures.Add($"A2A:Push:Proxy is '{options.Proxy}', which is not an absolute URI. Set it to a full proxy address, for example http://proxy.contoso.com:3128, or leave it unset for no proxy.");
        }

        var lifetime = options.Signature.TokenLifetime;
        if (lifetime < MinTokenLifetime || lifetime > MaxTokenLifetime)
        {
            failures.Add($"A2A:Push:Signature:TokenLifetime is {Show(lifetime)}, which is outside the allowed range of 1 second to 10 minutes. Receivers refuse longer lifetimes. Set it within that range; the default is 5 minutes.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string Show(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    private static bool IsValidCidr(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0) return false;
        if (!IPAddress.TryParse(value.AsSpan(0, slash), out var address)) return false;
        if (!int.TryParse(value.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)) return false;
        var max = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        return prefix >= 0 && prefix <= max;
    }
}

/// <summary>Runs <see cref="PushOptionsValidator"/> on the <see cref="PushOptions"/> held by <see cref="Hosting.A2AServerOptions.Push"/>, the instance delivery really uses.</summary>
internal sealed class PushOptionsInServerOptionsValidator : IValidateOptions<Hosting.A2AServerOptions>
{
    public ValidateOptionsResult Validate(string? name, Hosting.A2AServerOptions options) =>
        new PushOptionsValidator().Validate(name, options.Push);
}
