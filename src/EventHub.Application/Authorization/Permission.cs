namespace EventHub.Application.Authorization;

/// <summary>
/// A named permission (AD-8, AD-27): <c>&lt;Aggregate&gt;.&lt;Action&gt;</c>, for example <c>Event.Publish</c>.
/// Feature stories add their permissions and the matrix grants that use them.
/// </summary>
public readonly record struct Permission
{
    public Permission(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name ?? string.Empty;
}
