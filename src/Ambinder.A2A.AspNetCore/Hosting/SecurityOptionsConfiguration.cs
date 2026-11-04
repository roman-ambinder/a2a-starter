using Ambinder.A2A.Server.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Ambinder.A2A.AspNetCore.Hosting;

/// <summary>
/// Binds <see cref="A2ASecurityOptions"/> from the <c>A2A:Security</c> configuration section. Reads the values by hand, so the
/// package stays trim and AOT safe (the reflection-based binder is not).
/// </summary>
internal sealed class SecurityOptionsConfiguration(IConfiguration configuration) : IConfigureOptions<A2ASecurityOptions>
{
    public const string SectionPath = "A2A:Security";

    public void Configure(A2ASecurityOptions options)
    {
        var section = configuration.GetSection(SectionPath);
        if (ReadBool(section, nameof(A2ASecurityOptions.KeepCallerToken)) is { } keep) options.KeepCallerToken = keep;
        if (ReadBool(section, nameof(A2ASecurityOptions.FilterExtendedCardSkills)) is { } filter) options.FilterExtendedCardSkills = filter;
        ReadLists(section.GetSection(nameof(A2ASecurityOptions.Tenants)), options.Tenants);
        ReadLists(section.GetSection(nameof(A2ASecurityOptions.TenantBindings)), options.TenantBindings);
    }

    private static bool? ReadBool(IConfigurationSection section, string key)
    {
        var text = section[key];
        if (text is null) return null;
        return bool.TryParse(text, out var value)
            ? value
            : throw new InvalidOperationException(
                $"The configuration value '{SectionPath}:{key}' is '{text}', which is not a boolean. Use true or false.");
    }

    private static void ReadLists(IConfigurationSection section, IDictionary<string, string[]> target)
    {
        foreach (var entry in section.GetChildren())
        {
            var values = entry.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToArray();
            if (values.Length == 0 && !string.IsNullOrEmpty(entry.Value)) values = [entry.Value];
            target[entry.Key] = values;
        }
    }
}
