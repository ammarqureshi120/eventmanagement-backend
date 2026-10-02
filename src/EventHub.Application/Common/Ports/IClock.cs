namespace EventHub.Application.Common.Ports;

/// <summary>The only source of server "now" (AD-12). Always UTC.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
