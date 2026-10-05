namespace EventHub.Application.Common.Ports;

/// <summary>
/// The only source of entity ids (AD-13): SQL-Server-ordered sequential GUIDs, assigned in Application before
/// insert and passed into aggregate factories. Never <c>Guid.CreateVersion7()</c>.
/// </summary>
public interface IIdGenerator
{
    Guid NewId();
}
