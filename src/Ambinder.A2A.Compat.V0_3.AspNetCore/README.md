# Ambinder.A2A.Compat.V0_3.AspNetCore

A2A 0.3 compatibility for the [Ambinder.A2A](https://github.com/roman-ambinder/a2a-starter#readme) server. One agent serves 0.3 and 1.0 clients from the same handler at the same URLs. 0.3 requests are converted to 1.0 models and run through the normal request handler and middleware.

```
dotnet add package Ambinder.A2A.Compat.V0_3.AspNetCore
```

Package status: skeleton. `AddA2AV0_3Compat()` and the 0.3 endpoints arrive in later tasks of the package 15 plan.

The client side is [Ambinder.A2A.Compat.V0_3](https://www.nuget.org/packages/Ambinder.A2A.Compat.V0_3).

Part of [Ambinder.A2A](https://github.com/roman-ambinder/a2a-starter#readme): see the repository for samples and guides.
