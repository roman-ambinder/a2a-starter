using System.Reflection;
using System.Runtime.CompilerServices;
using Ambinder.A2A.Client;
using Ambinder.A2A.Client.Grpc;
using Ambinder.A2A.Protocol;
using Ambinder.A2A.Server;
using Microsoft.AspNetCore.Builder;

namespace Ambinder.A2A.Interop.Tests;

/// <summary>Spec principle 2 check: the interop suite sees the shipped packages the way a product team does.</summary>
public class PublicApiOnlyTests
{
    // One public type per referenced src assembly.
    private static readonly Assembly[] Shipped =
    [
        typeof(AgentCard).Assembly,                              // Ambinder.A2A.Protocol
        typeof(IAgentHandler).Assembly,                          // Ambinder.A2A.Server
        typeof(A2AEndpointRouteBuilderExtensions).Assembly,      // Ambinder.A2A.AspNetCore
        typeof(A2AGrpcEndpointRouteBuilderExtensions).Assembly,  // Ambinder.A2A.AspNetCore.Grpc
        typeof(IA2AClient).Assembly,                             // Ambinder.A2A.Client
        typeof(GrpcClientTransport).Assembly,                    // Ambinder.A2A.Client.Grpc
    ];

    [Fact]
    public void No_shipped_assembly_grants_internals_to_interop_tests()
    {
        Assert.Equal(6, Shipped.Select(a => a.GetName().Name).Distinct().Count());
        foreach (var asm in Shipped)
            Assert.DoesNotContain(asm.GetCustomAttributes<InternalsVisibleToAttribute>(),
                a => a.AssemblyName.StartsWith("Ambinder.A2A.Interop", StringComparison.Ordinal)
                  || a.AssemblyName.StartsWith("Ambinder.A2A.Itk", StringComparison.Ordinal));
    }
}
