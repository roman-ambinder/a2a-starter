using A2A.Protocol;
using A2A.Server.Transports;

namespace A2A.Server.Tests.Transports;

/// <summary>Spec 3.6.2: an empty version means A2A 0.3. The HTTP transports trim the header first, so whitespace is tested here.</summary>
public class ServiceParametersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_version_gets_the_0_3_message(string? version)
    {
        var error = Assert.Throws<A2AException>(() => ServiceParameters.EnsureSupportedVersion(version));

        Assert.Equal(A2AErrorCode.VersionNotSupported, error.Code);
        Assert.Contains("treated as A2A 0.3", error.Message, StringComparison.Ordinal);
    }
}
