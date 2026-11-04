using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>EventIds are public API (spec 02 section 1, item 7): unique per assembly and inside the assembly's fixed block.</summary>
public sealed class LogEventIdTests
{
    // First id of each block. Each block has 1000 ids. The full table is in docs/policies/versioning.md.
    private static readonly Dictionary<string, int> Blocks = new(StringComparer.Ordinal)
    {
        ["Ambinder.A2A.Server"] = 1000,
        ["Ambinder.A2A.AspNetCore"] = 2000,
        ["Ambinder.A2A.AspNetCore.Grpc"] = 3000,
        ["Ambinder.A2A.Client"] = 4000,
        ["Ambinder.A2A.Client.Grpc"] = 5000,
        ["Ambinder.A2A.Client.Entra"] = 6000,
    };

    public static TheoryData<string> AssemblyNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in ShippedAssemblies.ProjectNames.Where(n => n != "Ambinder.A2A.Protocol"))
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AssemblyNames))]
    public void Event_ids_are_unique_and_inside_the_block(string assemblyName)
    {
        Assert.True(Blocks.TryGetValue(assemblyName, out var first), $"{assemblyName} has no EventId block; add it to this test and to docs/policies/versioning.md.");
        var assembly = ShippedAssemblies.All.Single(a => a.GetName().Name == assemblyName);
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var ids = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(All))
            .Select(m => (m, attribute: m.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(x => x.attribute is not null)
            .Select(x => (Name: x.m.DeclaringType!.FullName + "." + x.m.Name, Id: x.attribute!.EventId))
            .ToArray();

        if (assemblyName is "Ambinder.A2A.Server" or "Ambinder.A2A.AspNetCore" or "Ambinder.A2A.AspNetCore.Grpc")
        {
            Assert.NotEmpty(ids); // the scan itself works: these assemblies log
        }

        Assert.Equal(ids.Length, ids.Select(x => x.Id).Distinct().Count());
        foreach (var (name, id) in ids)
        {
            Assert.True(id >= first && id < first + 1000, $"{name} has EventId {id}; the block of {assemblyName} is {first} to {first + 999}.");
        }
    }
}
