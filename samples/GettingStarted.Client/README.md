# GettingStarted.Client

The smallest A2A client: read the agent card, create a client, send one message, print the reply text. `Program.cs` needs only the
`Ambinder.A2A.Client` package. To use it in your own project: `dotnet new console`, `dotnet add package Ambinder.A2A.Client`, and
paste the file over `Program.cs`.

Start the agent first (see [GettingStarted.Agent](../GettingStarted.Agent/README.md)), then:

```bash
dotnet run --project samples/GettingStarted.Client                              # sends "hello" to http://localhost:5100
dotnet run --project samples/GettingStarted.Client -- http://localhost:5100 Ada # agent URL, then the text to send
```

It prints the reply (`Hello, Ada!`) and exits with 0. It exits with 1 when the agent's reply has no text.
