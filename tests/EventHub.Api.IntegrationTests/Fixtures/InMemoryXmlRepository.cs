using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>
/// Test-only key store for DB-free hosts: the key-ring preload never touches the (unreachable) database.
/// </summary>
public sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly List<XElement> _elements = [];

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_elements)
        {
            return _elements.Select(element => new XElement(element)).ToList();
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_elements)
        {
            _elements.Add(new XElement(element));
        }
    }
}
