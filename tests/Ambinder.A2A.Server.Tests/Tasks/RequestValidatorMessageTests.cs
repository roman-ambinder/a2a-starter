using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Tasks;

namespace Ambinder.A2A.Server.Tests.Tasks;

/// <summary>
/// Design D4, principle 3: an error message says what the caller should do. The full text is pinned here.
/// These sit with the Server tests (not next to ErrorMessageTests in Integration.Tests) because
/// <c>RequestValidator</c> is compiled into both Ambinder.A2A.Server and Ambinder.A2A.AspNetCore, so the Integration tests see it twice.
/// </summary>
public class RequestValidatorMessageTests
{
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
