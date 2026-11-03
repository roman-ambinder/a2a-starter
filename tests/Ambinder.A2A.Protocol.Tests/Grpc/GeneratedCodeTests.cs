namespace Ambinder.A2A.Protocol.Tests.Grpc;

public class GeneratedCodeTests
{
    [Fact]
    public void Generated_service_has_all_eleven_operations()
    {
        var methods = Lf.A2a.V1.A2AService.Descriptor.Methods.Select(m => m.Name).ToHashSet();
        Assert.Equal(new HashSet<string>
        {
            "SendMessage", "SendStreamingMessage", "GetTask", "ListTasks", "CancelTask",
            "SubscribeToTask", "CreateTaskPushNotificationConfig", "GetTaskPushNotificationConfig",
            "ListTaskPushNotificationConfigs", "DeleteTaskPushNotificationConfig", "GetExtendedAgentCard"
        }, methods);
    }
}
