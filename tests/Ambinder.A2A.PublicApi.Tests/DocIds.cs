using System.Reflection;
using System.Text;

namespace Ambinder.A2A.PublicApi.Tests;

/// <summary>Maps a reflected member to its XML documentation ID (<c>T:</c>, <c>M:</c>, <c>P:</c>, <c>F:</c>, <c>E:</c>).</summary>
internal static class DocIds
{
    public static string For(MemberInfo member) => member switch
    {
        Type t => "T:" + TypeDefinitionName(t),
        ConstructorInfo c => "M:" + TypeDefinitionName(c.DeclaringType!) + "." + (c.IsStatic ? "#cctor" : "#ctor") + Parameters(c),
        MethodInfo m => "M:" + TypeDefinitionName(m.DeclaringType!) + "." + m.Name + (m.IsGenericMethodDefinition ? "``" + m.GetGenericArguments().Length : "") + Parameters(m),
        PropertyInfo p => "P:" + TypeDefinitionName(p.DeclaringType!) + "." + p.Name + IndexerParameters(p),
        FieldInfo f => "F:" + TypeDefinitionName(f.DeclaringType!) + "." + f.Name,
        EventInfo e => "E:" + TypeDefinitionName(e.DeclaringType!) + "." + e.Name,
        _ => throw new NotSupportedException(member.GetType().Name),
    };

    /// <summary>The name of a type in its own declaration: nested types use <c>.</c>, generic types keep <c>`N</c>.</summary>
    private static string TypeDefinitionName(Type t) => t.FullName!.Replace('+', '.');

    private static string Parameters(MethodBase m)
    {
        var ps = m.GetParameters();
        return ps.Length == 0 ? "" : "(" + string.Join(",", ps.Select(p => TypeRef(p.ParameterType))) + ")";
    }

    private static string IndexerParameters(PropertyInfo p)
    {
        var ps = p.GetIndexParameters();
        return ps.Length == 0 ? "" : "(" + string.Join(",", ps.Select(x => TypeRef(x.ParameterType))) + ")";
    }

    /// <summary>A type as it appears in a parameter list.</summary>
    private static string TypeRef(Type t)
    {
        if (t.IsByRef) return TypeRef(t.GetElementType()!) + "@";
        if (t.IsPointer) return TypeRef(t.GetElementType()!) + "*";
        if (t.IsArray) return TypeRef(t.GetElementType()!) + "[]";
        if (t.IsGenericMethodParameter) return "``" + t.GenericParameterPosition;
        if (t.IsGenericTypeParameter) return "`" + t.GenericParameterPosition;
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
        {
            var name = t.GetGenericTypeDefinition().FullName!.Replace('+', '.');
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            var sb = new StringBuilder(tick < 0 ? name : name[..tick]);
            sb.Append('{').Append(string.Join(",", t.GetGenericArguments().Select(TypeRef))).Append('}');
            return sb.ToString();
        }

        return t.FullName!.Replace('+', '.');
    }
}
