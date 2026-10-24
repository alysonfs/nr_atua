using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemColetaEncerradaEmUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ColetaEncerradaEmUtc",
                table: "triagens",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill: triagens já encerradas (status fora do fluxo ativo de
            // coleta) recebem o encerramento da coleta como o próprio
            // UpdatedAtUtc, já que o carimbo específico não existia antes
            // desta migration.
            migrationBuilder.Sql(
                """
                UPDATE triagens
                SET "ColetaEncerradaEmUtc" = "UpdatedAtUtc"
                WHERE "Status" NOT IN ('AguardandoEnvio', 'ContatoIniciado', 'EmColeta')
                  AND "ColetaEncerradaEmUtc" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColetaEncerradaEmUtc",
                table: "triagens");
        }
    }
}
