using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Emenda ADR-018 ("Resolução de integrationId"): insere o
    /// <c>IntegrationProvider</c> "iService" com Id fixo e conhecido
    /// (<see cref="Atua.Api.Domain.Integrations.WellKnownIntegrationProviders.IServiceProviderId"/>),
    /// permitindo que <c>TenantOnboardingService</c> crie automaticamente a
    /// <c>Integration</c> do provedor iService ao criar um Tenant, sem exigir
    /// nenhum endpoint de descoberta/criação de integração no MVP (onde há
    /// exatamente um provedor). Gerada via <c>dotnet ef migrations add</c>
    /// (o seed é declarado com <c>HasData</c> em
    /// <c>AtuaDbContext.ConfigureIntegrationProvider</c> para que o EF Core
    /// o reconheça no model snapshot e não o trate como drift em migrations
    /// futuras).
    /// </summary>
    public partial class SeedIServiceIntegrationProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "integration_providers",
                columns: new[] { "Id", "BaseUri", "IsActive", "Manufacturer", "Name" },
                values: new object[] { new Guid("00000000-0000-0000-0000-0000000000e1"), "https://iservice.example.com/", true, "iService", "iService" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "integration_providers",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-0000000000e1"));
        }
    }
}
