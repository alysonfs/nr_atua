using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atua.Api.Infrastructure.Persistence;

public sealed class AtuaDbContextFactory : IDesignTimeDbContextFactory<AtuaDbContext>
{
    public AtuaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Atua")
            ?? "Host=localhost;Database=atua;Username=atua";
        var options = new DbContextOptionsBuilder<AtuaDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AtuaDbContext(options);
    }
}