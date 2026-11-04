using System.Globalization;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.Server.Hosting;

/// <summary>
/// Checks <see cref="A2AServerOptions"/> when the options are first used. Each message names the property, shows the bad value
/// and says what to set (principle 3: errors tell the caller what to do).
/// </summary>
internal sealed class A2AServerOptionsValidator : IValidateOptions<A2AServerOptions>
{
    public ValidateOptionsResult Validate(string? name, A2AServerOptions options)
    {
        List<string>? failures = null;

        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            var shown = string.IsNullOrEmpty(options.PublicBaseUrl) ? "empty" : $"'{options.PublicBaseUrl}'";
            (failures ??= []).Add(
                $"A2AServerOptions.PublicBaseUrl is {shown}, which is not an absolute http(s) URL. " +
                "Set PublicBaseUrl in the callback you pass to AddA2AServer (or AddA2AServerCore) to the URL clients use to reach this server, " +
                "for example https://agents.contoso.com.");
        }

        if (options.MaxRequestBodyBytes <= 0)
        {
            (failures ??= []).Add(
                $"A2AServerOptions.MaxRequestBodyBytes is {options.MaxRequestBodyBytes.ToString(CultureInfo.InvariantCulture)}, which is not positive. " +
                "Set MaxRequestBodyBytes to the largest request body to accept, in bytes, for example 4194304 (4 MiB), or leave it unset for that default.");
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
