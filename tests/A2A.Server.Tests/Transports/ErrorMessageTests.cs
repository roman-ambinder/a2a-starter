using A2A.Protocol;
using A2A.Server.Tasks;
using A2A.Server.Transports;

namespace A2A.Server.Tests.Transports;

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

    [Fact]
    public void Context_id_too_long()
    {
        var error = Assert.Throws<A2AException>(() => RequestValidator.ValidateContextId(new string('c', 257)));
        Assert.Equal("message.contextId is longer than 256 characters. Send a shorter id, or omit it to get a new context.", error.Message);
    }

    [Fact]
    public void Context_id_with_control_characters()
    {
        var error = Assert.Throws<A2AException>(() => RequestValidator.ValidateContextId("a\nb"));
        Assert.Equal("message.contextId contains control characters. Send a printable id, or omit it to get a new context.", error.Message);
    }

    [Fact]
    public void Required_extension_not_requested()
    {
        var card = new AgentCard
        {
            Name = "a", Description = "d", Version = "1",
            Capabilities = new AgentCapabilities { Extensions = [new AgentExtension { Uri = "urn:x", Required = true }] },
        };
        var error = Assert.Throws<A2AException>(() => RequestValidator.ResolveExtensions(card, []));
        Assert.Equal("Required extension(s) not requested: urn:x. Send them in the A2A-Extensions header.", error.Message);
    }
}
