using Ambinder.A2A.Protocol;
using Ambinder.A2A.AspNetCore.Transports;

namespace Ambinder.A2A.Integration.Tests.Transport;

/// <summary>Design D4, principle 3: an error message says what the caller should do. The full text is pinned here.</summary>
public class ErrorMessageTests
{
    [Fact]
    public void Missing_version_header()
    {
        var error = Assert.Throws<A2AException>(() => ServiceParameters.EnsureSupportedVersion(null));
        Assert.Equal("No A2A-Version header was sent, so the request is treated as A2A 0.3. This agent supports 1.0. " +
                     "Send the header 'A2A-Version: 1.0'.", error.Message);
    }

    [Fact]
    public void Unsupported_version()
    {
        var error = Assert.Throws<A2AException>(() => ServiceParameters.EnsureSupportedVersion("2.0"));
        Assert.Equal("A2A version '2.0' is not supported. Supported: 1.0. Send the header 'A2A-Version: 1.0'.", error.Message);
    }
}
