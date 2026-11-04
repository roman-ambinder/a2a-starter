// Owned by package 07; minimal version added by 05.
namespace Ambinder.A2A.Server.Security;

/// <summary>
/// The key that identifies who owns a task. The string form is frozen at 1.0 because every stored task holds it.
/// </summary>
public readonly record struct OwnerKey
{
    private readonly string? _value;

    private OwnerKey(string value) => _value = value;

    /// <summary>The string form of the key, for example <c>tid:{tid}/app:{appid}</c>. Empty for a default value.</summary>
    public string Value => _value ?? "";

    /// <summary>Builds the key for an application (app-only token): <c>tid:{tenantId}/app:{appId}</c>.</summary>
    /// <param name="tenantId">The Entra tenant id.</param>
    /// <param name="appId">The application (client) id.</param>
    /// <returns>The owner key.</returns>
    public static OwnerKey ForApp(string tenantId, string appId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(appId);
        return new OwnerKey($"tid:{tenantId}/app:{appId}");
    }

    /// <summary>Builds the key for a user (delegated token): <c>tid:{tenantId}/user:{userObjectId}</c>.</summary>
    /// <param name="tenantId">The Entra tenant id.</param>
    /// <param name="userObjectId">The user's object id.</param>
    /// <returns>The owner key.</returns>
    public static OwnerKey ForUser(string tenantId, string userObjectId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(userObjectId);
        return new OwnerKey($"tid:{tenantId}/user:{userObjectId}");
    }

    /// <summary>Wraps an existing owner key string, for example one read from a store or built by another scheme's resolver.</summary>
    /// <param name="value">The owner key string. Must not be empty.</param>
    /// <returns>The owner key.</returns>
    public static OwnerKey Parse(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return new OwnerKey(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
