namespace Atua.Api.Domain.Integrations;

public sealed class IntegrationProvider
{
    private IntegrationProvider()
    {
    }

    public IntegrationProvider(Guid id, string name, string manufacturer,
        Uri baseUri, bool isActive)
    {
        Id = id;
        Name = name;
        Manufacturer = manufacturer;
        BaseUri = baseUri;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Manufacturer { get; private set; } = null!;

    public Uri BaseUri { get; private set; } = null!;

    public bool IsActive { get; private set; }
}