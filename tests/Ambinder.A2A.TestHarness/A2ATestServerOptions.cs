using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;

namespace Ambinder.A2A.Testing;

/// <summary>Settings for <see cref="A2ATestServer.StartAsync"/>.</summary>
public sealed class A2ATestServerOptions
{
    /// <summary>The start time of the fake clock.</summary>
    public DateTimeOffset StartTime { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The hosting environment name.</summary>
    public string Environment { get; set; } = "Development";

    /// <summary>Runs on the web host builder before the host is built, for example to add middleware services.</summary>
    public Action<IWebHostBuilder>? ConfigureWebHost { get; set; }

    /// <summary>Runs on the built app before <c>MapA2A</c>, for example to add middleware.</summary>
    public Action<WebApplication>? ConfigureApp { get; set; }
}
