namespace Ambinder.A2A.Server;

/// <summary>The A2A operations a middleware can see. Every call to <see cref="IA2ARequestHandler"/> maps to one of them.</summary>
/// <remarks>More values can be added in later versions; handle unknown values.</remarks>
public enum A2AOperation
{
    /// <summary>Send a message and wait for the reply.</summary>
    SendMessage,

    /// <summary>Send a message and stream the events.</summary>
    SendStreamingMessage,

    /// <summary>Get one task.</summary>
    GetTask,

    /// <summary>Page through the tasks the caller owns.</summary>
    ListTasks,

    /// <summary>Cancel a task.</summary>
    CancelTask,

    /// <summary>Follow a task and stream its events.</summary>
    SubscribeToTask,

    /// <summary>Create a push notification configuration.</summary>
    CreateTaskPushNotificationConfig,

    /// <summary>Get one push notification configuration.</summary>
    GetTaskPushNotificationConfig,

    /// <summary>List the push notification configurations of a task.</summary>
    ListTaskPushNotificationConfigs,

    /// <summary>Delete a push notification configuration.</summary>
    DeleteTaskPushNotificationConfig,

    /// <summary>Get the extended agent card.</summary>
    GetExtendedAgentCard,
}
