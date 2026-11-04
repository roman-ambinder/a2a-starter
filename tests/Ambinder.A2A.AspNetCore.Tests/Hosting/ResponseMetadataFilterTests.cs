using Ambinder.A2A.AspNetCore.Transports;
using Ambinder.A2A.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ambinder.A2A.AspNetCore.Tests.Hosting;

public sealed class ResponseMetadataFilterTests
{
    private static List<KeyValuePair<string, string>> Run(bool grpc, params (string Name, string Value)[] entries)
    {
        var metadata = new A2AResponseMetadata();
        foreach (var (name, value) in entries) metadata.Set(name, value);
        return ResponseMetadataFilter.Allowed(metadata, new HashSet<string>(StringComparer.OrdinalIgnoreCase), NullLogger.Instance, grpc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DropsNamesWithControlCharactersAndValuesWithLineBreaks(bool grpc)
    {
        var result = Run(grpc, ("A2A-Bad\r\nX", "v"), ("A2A-Sp ace", "v"), ("A2A-V", "a\r\nB: c"), ("A2A-V2", "a\0b"), ("a2a-ok", "fine"));

        var entry = Assert.Single(result);
        Assert.Equal("a2a-ok", entry.Key);
    }

    [Fact]
    public void GrpcRejectsNonAsciiValuesHttpKeepsTabs()
    {
        Assert.Empty(Run(grpc: true, ("A2A-V", "caf\u00e9")));
        Assert.Empty(Run(grpc: true, ("A2A-V", "a\tb")));
        Assert.Single(Run(grpc: false, ("A2A-V", "a\tb")));
    }
}
