namespace MockEntra;

public sealed class MockEntraOptions
{
    public string IssuerBaseUrl { get; set; } = "http://localhost:5100";
    public string TenantId { get; set; } = "";
    public string Audience { get; set; } = "";
    public int TokenLifetimeSeconds { get; set; } = 3600;
    public List<MockClient> Clients { get; set; } = [];
}

public sealed class MockClient
{
    public string ClientId { get; set; } = "";
    public string Secret { get; set; } = "";
    public List<string> Roles { get; set; } = [];
}
