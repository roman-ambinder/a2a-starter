using System.Runtime.CompilerServices;
using Ambinder.A2A.Client.Tests.TestDoubles;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Client.Tests;

// Task 4 covers the seam types and the scripted double. Task 12 adds the client-level acceptance test
// ("a fake factory for version 9.9 is selected from a card and gets interceptors, auth and tracing").
public sealed class TransportSeamTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static A2ATransportRequest NewRequest(Action<A2AResponseInfo>? onHeaders = null, string method = "SendMessage") => new()
    {
        Method = method,
        Body = new SendMessageRequest(),
        Headers = new A2ARequestHeaders(),
        ProtocolVersion = ScriptedTransportFactory.Version,
        CallInfo = new A2ACallInfo
        {
            Method = method,
            ClientName = "default",
            Safety = A2ARetrySafety.NotSafe,
            Timeout = TimeSpan.FromSeconds(30),
            TimeoutOption = "A2AClientOptions.Timeouts.Unary",
        },
        OnHeaders = onHeaders,
    };

    private static (ScriptedTransportFactory Factory, IA2ATransport Transport) Open()
    {
        var factory = new ScriptedTransportFactory();
        var card = Cards.Single(ScriptedTransportFactory.Binding, ScriptedTransportFactory.Version, "test://agent");
        using var http = new HttpClient();
        var transport = factory.Create(card.SupportedInterfaces[0], card, ScriptedTransportFactory.NewContext(http));
        return (factory, transport);
    }

    [Fact]
    public void Factory_reports_its_bindings_and_versions()
    {
        var factory = new ScriptedTransportFactory();
        Assert.Equal(["TEST"], factory.Bindings);
        Assert.Equal(["9.9"], factory.ProtocolVersions);
    }

    [Fact]
    public void Factory_receives_the_selected_interface_card_and_context()
    {
        var factory = new ScriptedTransportFactory();
        var card = Cards.Single("TEST", "9.9", "test://agent", tenant: "acme");
        using var http = new HttpClient();
        var context = ScriptedTransportFactory.NewContext(http);
        var transport = factory.Create(card.SupportedInterfaces[0], card, context);
        Assert.NotNull(transport);
        Assert.Equal(1, factory.Created);
        Assert.Same(context, Assert.Single(factory.Contexts));
        Assert.Same(http, context.HttpClient);
        Assert.Null(context.HandlerFactory);
    }

    [Fact]
    public async Task Send_records_the_request_and_reports_headers()
    {
        var (factory, transport) = Open();
        A2AResponseInfo? seen = null;
        var request = NewRequest(i => seen = i);
        request.Headers["X-Trace"] = "1";

        var response = await transport.SendAsync(request, Ct);

        Assert.IsType<SendMessageResponse>(response.Body);
        var recorded = Assert.Single(factory.Requests);
        Assert.Same(request, recorded);
        Assert.Equal("SendMessage", recorded.Method);
        Assert.Equal("9.9", recorded.ProtocolVersion);
        Assert.Equal("1", recorded.Headers["X-Trace"]);
        Assert.Same(response.Info, seen);
        Assert.Equal("TEST", response.Info.Transport);
    }

    [Fact]
    public async Task Send_honors_cancellation()
    {
        var (_, transport) = Open();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await transport.SendAsync(NewRequest(), cts.Token));
    }

    [Fact]
    public async Task Stream_reports_headers_before_the_first_event_and_yields_in_order()
    {
        var (factory, transport) = Open();
        var first = new StreamResponse { Message = new Message { MessageId = "1", Role = Role.Agent, Parts = [new Part { Text = "a" }] } };
        var second = new StreamResponse { Message = new Message { MessageId = "2", Role = Role.Agent, Parts = [new Part { Text = "b" }] } };
        factory.StreamEvents = [first, second];
        var order = new List<string>();

        await foreach (var e in transport.StreamAsync(NewRequest(_ => order.Add("headers"), "SendStreamingMessage"), Ct))
        {
            order.Add(e.Message!.MessageId);
        }

        Assert.Equal(["headers", "1", "2"], order);
    }

    [Fact]
    public async Task Disposing_the_transport_is_observable()
    {
        var (factory, transport) = Open();
        await transport.DisposeAsync();
        Assert.Equal(1, factory.Disposed);
    }

    [Theory]
    [InlineData(typeof(A2ATransportRequest))]
    [InlineData(typeof(A2ATransportResponse))]
    [InlineData(typeof(A2ATransportContext))]
    [InlineData(typeof(A2ACallInfo))]
    public void Seam_types_are_not_positional(Type type)
    {
        Assert.True(type.IsSealed);
        Assert.DoesNotContain(type.GetConstructors(), c => c.GetParameters().Length > 0);
        Assert.Null(type.GetMethod("<Clone>$"));   // not a record
    }

    [Fact]
    public void Required_members_are_marked_required()
    {
        foreach (var type in new[] { typeof(A2ATransportRequest), typeof(A2ATransportResponse), typeof(A2ATransportContext), typeof(A2ACallInfo) })
        {
            Assert.NotEmpty(type.GetCustomAttributes(typeof(RequiredMemberAttribute), inherit: false));
        }
    }

    [Fact]
    public void Limits_and_timeouts_use_the_spec_defaults()
    {
        var limits = new A2AClientLimits();
        Assert.Equal(8L * 1024 * 1024, limits.MaxResponseBytes);
        Assert.Equal(4 * 1024 * 1024, limits.MaxEventBytes);
        Assert.Equal(8 * 1024 * 1024, limits.MaxGrpcMessageBytes);
        Assert.Equal(64L * 1024, limits.MaxErrorBodyBytes);

        var timeouts = new A2AClientTimeouts();
        Assert.Equal(TimeSpan.FromSeconds(30), timeouts.Unary);
        Assert.Equal(TimeSpan.FromSeconds(30), timeouts.NonBlockingSend);
        Assert.Equal(TimeSpan.FromMinutes(10), timeouts.BlockingSend);
        Assert.Equal(TimeSpan.FromMinutes(5), timeouts.StreamIdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), timeouts.CardFetch);
    }
}
