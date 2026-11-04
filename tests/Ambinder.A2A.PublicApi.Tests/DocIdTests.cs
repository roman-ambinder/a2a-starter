using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server.Hosting;

namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>The three IDs below are copied from the generated Ambinder.A2A.*.xml files.</summary>
public class DocIdTests
{
    [Fact]
    public void Constructor_id()
    {
        var ctor = typeof(A2AException).GetConstructors().Single();
        Assert.Equal(
            "M:Ambinder.A2A.Protocol.A2AException.#ctor(Ambinder.A2A.Protocol.A2AErrorCode,System.String,System.Nullable{System.Text.Json.JsonElement})",
            DocIds.For(ctor));
    }

    [Fact]
    public void Generic_method_id()
    {
        var method = typeof(A2AServerBuilder).GetMethod(nameof(A2AServerBuilder.AddAgent))!;
        Assert.Equal(
            "M:Ambinder.A2A.Server.Hosting.A2AServerBuilder.AddAgent``1(System.String,System.Func{System.IServiceProvider,Ambinder.A2A.Protocol.AgentCard},System.Func{System.IServiceProvider,Ambinder.A2A.Protocol.AgentCard},System.String)",
            DocIds.For(method));
    }

    [Fact]
    public void Method_with_cancellation_token_id()
    {
        var method = typeof(Ambinder.A2A.Client.Auth.ApiKeyCredentialProvider).GetMethod("GetHeadersAsync")!;
        Assert.Equal(
            "M:Ambinder.A2A.Client.Auth.ApiKeyCredentialProvider.GetHeadersAsync(Ambinder.A2A.Protocol.AgentCard,System.Threading.CancellationToken)",
            DocIds.For(method));
    }

    [Fact]
    public void Property_field_and_type_ids()
    {
        Assert.Equal("P:Ambinder.A2A.Protocol.SendMessageRequest.Tenant", DocIds.For(typeof(SendMessageRequest).GetProperty("Tenant")!));
        Assert.Equal("F:Ambinder.A2A.Protocol.A2AConstants.ProtocolVersion", DocIds.For(typeof(A2AConstants).GetField("ProtocolVersion")!));
        Assert.Equal("T:Ambinder.A2A.Protocol.ProtoEnumConverter`1", DocIds.For(typeof(ProtoEnumConverter<>)));
    }
}
