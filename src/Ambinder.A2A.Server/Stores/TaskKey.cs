namespace Ambinder.A2A.Server.Stores;

/// <summary>Identifies a task. <paramref name="Tenant"/> is <c>""</c> when the deployment is single-tenant; never null.</summary>
/// <param name="Tenant">The tenant, or <c>""</c> for a single-tenant deployment.</param>
/// <param name="Agent">The agent name.</param>
/// <param name="TaskId">The task id, a server-made GUID.</param>
public readonly record struct TaskKey(string Tenant, string Agent, string TaskId);

/// <summary>What a caller-scoped query is limited to.</summary>
/// <param name="Tenant">The tenant, or <c>""</c> for a single-tenant deployment.</param>
/// <param name="Agent">The agent name.</param>
/// <param name="Owner">The owner key string of the caller.</param>
public readonly record struct OwnerScope(string Tenant, string Agent, string Owner);

/// <summary>A task key plus its owner, taken from a loaded <see cref="StoredTask"/>. Lets owner-partitioned stores find the task's partition.</summary>
/// <param name="Key">The task key.</param>
/// <param name="Owner">The owner key string of the task's owner.</param>
public readonly record struct TaskRef(TaskKey Key, string Owner);
