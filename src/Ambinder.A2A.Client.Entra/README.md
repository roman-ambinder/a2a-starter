# Ambinder.A2A.Client.Entra

Azure.Identity credentials for [Ambinder.A2A.Client](../Ambinder.A2A.Client/README.md). `AzureIdentityCredentialProvider` gets bearer tokens from an Azure.Identity `TokenCredential` (which does its own caching). Reference the project; it depends on Ambinder.A2A.Client and `Azure.Identity`.

## Minimal use

```csharp
using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Entra;
using Azure.Identity;

var options = new A2AClientOptions
{
    Credentials = new AzureIdentityCredentialProvider(new DefaultAzureCredential(), "api://my-agent/.default"),
};
```
