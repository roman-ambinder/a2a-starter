# Ambinder.A2A.Client.Entra

Azure.Identity credentials for [Ambinder.A2A.Client](https://www.nuget.org/packages/Ambinder.A2A.Client). `AzureIdentityCredentialProvider` gets bearer tokens from an Azure.Identity `TokenCredential` (which does its own caching) and sends them as the `Authorization` header. It depends on Ambinder.A2A.Client and `Azure.Identity`.

```
dotnet add package Ambinder.A2A.Client.Entra
```

## Example

```csharp
using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Entra;
using Azure.Identity;

var options = new A2AClientOptions
{
    Credentials = new AzureIdentityCredentialProvider(new DefaultAzureCredential(), "api://my-agent/.default"),
};
// then: A2AClientFactory.Create(card, options)
```

Part of [Ambinder.A2A](https://github.com/roman-ambinder/a2a-starter#readme): see the repository for samples and guides.
