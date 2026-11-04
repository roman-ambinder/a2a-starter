# GettingStarted.Agent

The smallest A2A agent: one handler that answers "Hello, <your text>!". `Program.cs` is about 20 lines and needs only the
`Ambinder.A2A.AspNetCore` package. To use it in your own project: `dotnet new web`, `dotnet add package Ambinder.A2A.AspNetCore`,
and paste the file over `Program.cs`.

```bash
dotnet run --project samples/GettingStarted.Agent
```

The agent listens on `http://localhost:5100` (change it with `A2A:PublicBaseUrl`, for example
`--A2A:PublicBaseUrl=http://localhost:5400`). Its card is at <http://localhost:5100/.well-known/agent-card.json>.

In a second terminal, run the matching client:

```bash
dotnet run --project samples/GettingStarted.Client
```

See [GettingStarted.Client](../GettingStarted.Client/README.md).
