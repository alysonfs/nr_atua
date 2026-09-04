using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Atua.Api.Infrastructure.Persistence;

public sealed class AtuaDbContextFactory : IDesignTimeDbContextFactory<AtuaDbContext>
{
    public AtuaDbContext CreateDbContext(string[] args)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        // Lê configuração na mesma ordem de precedência do runtime ASP.NET Core:
        // appsettings.json → appsettings.{env}.json → user-secrets → variáveis de ambiente.
        // Isso permite que `dotnet ef` funcione sem exportar Postgres__ConnectionString manualmente.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{env}.json", optional: true)
            .AddUserSecrets<AtuaDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Mesma precedência do Program.cs: padrão novo primeiro, legado como fallback.
        var connectionString = configuration["Postgres:ConnectionString"]
            ?? configuration.GetConnectionString("Atua")
            ?? "Host=localhost;Port=5432;Database=atua;Username=atua";

        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AtuaDbContext(options);
    }
}