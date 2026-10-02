namespace EventHub.Domain.Common;

/// <summary>
/// Entities whose <c>CreatedAtUtc</c>/<c>UpdatedAtUtc</c> are stamped by the persistence
/// SaveChanges interceptor from <c>IClock</c> (AD-19). Never set these in feature code.
/// </summary>
public interface ITimestamped
{
    DateTime CreatedAtUtc { get; set; }

    DateTime UpdatedAtUtc { get; set; }
}
