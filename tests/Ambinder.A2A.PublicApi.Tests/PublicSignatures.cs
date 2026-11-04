using System.Reflection;

namespace Ambinder.A2A.PublicApi.Tests;

internal static class PublicSignatures
{
    /// <summary>One type used in a public or protected signature, with the member and the declaring type that use it.</summary>
    public readonly record struct Use(string Member, Type Type, Type Owner);

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static bool IsVisible(Type t)
        => t.IsPublic || (t.IsNestedPublic && IsVisible(t.DeclaringType!)) || (t.IsNestedFamily && IsVisible(t.DeclaringType!)) || (t.IsNestedFamORAssem && IsVisible(t.DeclaringType!));

    private static bool IsVisible(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;

    public static IEnumerable<Use> Walk(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(IsVisible))
        {
            var name = type.FullName!;
            if (type.BaseType is { } baseType) foreach (var u in Expand(name, baseType, type)) yield return u;
            foreach (var i in type.GetInterfaces()) foreach (var u in Expand(name, i, type)) yield return u;

            foreach (var m in type.GetMethods(Declared).Where(IsVisible))
            {
                foreach (var u in Expand($"{name}.{m.Name}", m.ReturnType, type)) yield return u;
                foreach (var p in m.GetParameters()) foreach (var u in Expand($"{name}.{m.Name}", p.ParameterType, type)) yield return u;
            }

            foreach (var c in type.GetConstructors(Declared).Where(IsVisible))
                foreach (var p in c.GetParameters()) foreach (var u in Expand($"{name}..ctor", p.ParameterType, type)) yield return u;

            foreach (var f in type.GetFields(Declared).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                foreach (var u in Expand($"{name}.{f.Name}", f.FieldType, type)) yield return u;

            // Properties and events are covered by their accessor methods above.
        }
    }

    private static IEnumerable<Use> Expand(string member, Type type, Type owner)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            foreach (var u in Expand(member, type.GetElementType()!, owner)) yield return u;
            yield break;
        }

        if (type.IsGenericParameter)
        {
            foreach (var c in type.GetGenericParameterConstraints()) foreach (var u in Expand(member, c, owner)) yield return u;
            yield break;
        }

        yield return new Use(member, type, owner);
        if (type.IsGenericType)
            foreach (var arg in type.GetGenericArguments()) foreach (var u in Expand(member, arg, owner)) yield return u;
    }
}
