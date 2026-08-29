namespace Atua.Api.Domain.Tenants;

public sealed class Tenant
{
    public Tenant(Guid id, string name, string cnpj, string timeZoneId)
    {
        Id = id;
        Name = name;
        Cnpj = cnpj;
        TimeZoneId = timeZoneId;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Cnpj { get; }

    public string TimeZoneId { get; }
}