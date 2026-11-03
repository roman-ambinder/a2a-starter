var builder = DistributedApplication.CreateBuilder(args);

// Each app runs from its own launch profile on its usual port. The Aspire proxy is off,
// so the ports stay 5100, 5300 and 5200 (AgentServer binds gRPC 5201-5204 itself).
var mockentra = builder.AddProject<Projects.MockEntra>("mockentra", launchProfileName: "MockEntra")
    .WithEndpoint("http", e => e.IsProxied = false)
    .WithHttpHealthCheck("/health/ready");

var webhookreceiver = builder.AddProject<Projects.WebhookReceiver>("webhookreceiver", launchProfileName: "WebhookReceiver")
    .WithEndpoint("http", e => e.IsProxied = false)
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(mockentra);

var agentserver = builder.AddProject<Projects.AgentServer>("agentserver", launchProfileName: "AgentServer")
    .WithEndpoint("http", e => e.IsProxied = false)
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(webhookreceiver);

// Runs every demo flow once. Click Start in the dashboard to run it.
builder.AddProject<Projects.AgentClient>("agentclient")
    .WithArgs("all")
    .WithExplicitStart()
    .WaitFor(agentserver);

builder.Build().Run();
