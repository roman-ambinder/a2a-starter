namespace Ambinder.A2A.Server.Security;

/// <summary>
/// The opaque key that identifies who owns a task, at most 256 characters long.
/// </summary>
/// <remarks>
/// <para>
/// The string format is part of the public contract and is frozen at 1.0, because every stored task holds it.
/// Stores treat the string as opaque and compare it ordinally.
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ForApp"/> builds <c>tid:{tenantId}/app:{appId}</c>.</description></item>
/// <item><description><see cref="ForUser"/> builds <c>tid:{tenantId}/user:{userObjectId}</c>.</description></item>
/// <item><description>Resolvers for other authentication schemes build their own strings through <see cref="Parse"/>, for example <c>apikey:{name}</c>.</description></item>
/// </list>
/// <para>
/// A <c>default</c> instance has an empty <see cref="Value"/> and is not a valid owner key. Equality is ordinal on <see cref="Value"/>.
/// </para>
/// </remarks>
public readonly record struct OwnerKey
{
    private const int MaxLength = 256;

    private readonly string? _value;

    private OwnerKey(string value) => _value = value;

    /// <summary>The key string, for example <c>tid:t1/app:a1</c>. It is never <see langword="null"/>.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Builds the key of an application (app-only token): <c>tid:{tenantId}/app:{appId}</c>.</summary>
    /// <param name="tenantId">The Entra tenant id. It must not be empty or whitespace only, or contain <c>/</c>.</param>
    /// <param name="appId">The application (client) id. It must not be empty or whitespace only, or contain <c>/</c>.</param>
    /// <returns>The owner key.</returns>
    /// <exception cref="ArgumentException">An argument is null, empty, whitespace only or contains <c>/</c>, or the key is longer than 256 characters.</exception>
    public static OwnerKey ForApp(string tenantId, string appId)
        => Build("app", tenantId, appId, nameof(tenantId), nameof(appId));

    /// <summary>Builds the key of a user (delegated token): <c>tid:{tenantId}/user:{userObjectId}</c>.</summary>
    /// <param name="tenantId">The Entra tenant id. It must not be empty or whitespace only, or contain <c>/</c>.</param>
    /// <param name="userObjectId">The user object id. It must not be empty or whitespace only, or contain <c>/</c>.</param>
    /// <returns>The owner key.</returns>
    /// <exception cref="ArgumentException">An argument is null, empty, whitespace only or contains <c>/</c>, or the key is longer than 256 characters.</exception>
    public static OwnerKey ForUser(string tenantId, string userObjectId)
        => Build("user", tenantId, userObjectId, nameof(tenantId), nameof(userObjectId));

    /// <summary>Wraps an owner key string, for example <c>apikey:ops</c> from a resolver for another authentication scheme.</summary>
    /// <param name="value">The key string. It must not be empty and must be at most 256 characters long.</param>
    /// <returns>The owner key.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty, whitespace only or longer than 256 characters.</exception>
    public static OwnerKey Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        CheckLength(value, nameof(value));
        return new OwnerKey(value);
    }

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The key string.</returns>
    public override string ToString() => Value;

    private static OwnerKey Build(string kind, string tenantId, string id, string tenantName, string idName)
    {
        CheckPart(tenantId, tenantName);
        CheckPart(id, idName);
        var value = $"tid:{tenantId}/{kind}:{id}";
        CheckLength(value, idName);
        return new OwnerKey(value);
    }

    private static void CheckPart(string part, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(part, name);
        if (part.Contains('/'))
        {
            throw new ArgumentException("The value must not contain '/', because it would make the owner key ambiguous.", name);
        }
    }

    private static void CheckLength(string value, string name)
    {
        if (value.Length > MaxLength)
        {
            throw new ArgumentException($"The owner key must be at most {MaxLength} characters long.", name);
        }
    }
}
