namespace Atua.Api.Domain.Tenants;

public sealed class Tenant
{
    public Tenant(Guid id, string name, string cnpj, string timeZoneId)
    {
        if (!IanaTimeZone.IsValid(timeZoneId))
        {
            throw new ArgumentException("Identificador de fuso horário IANA inválido.", nameof(timeZoneId));
        }

        Id = id;
        Name = name;
        Cnpj = cnpj;
        TimeZoneId = timeZoneId;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Cnpj { get; }

    public string TimeZoneId { get; private set; }

    public void SetTimeZone(string timeZoneId)
    {
        if (!IanaTimeZone.IsValid(timeZoneId))
        {
            throw new ArgumentException("Identificador de fuso horário IANA inválido.", nameof(timeZoneId));
        }

        TimeZoneId = timeZoneId;
    }
}