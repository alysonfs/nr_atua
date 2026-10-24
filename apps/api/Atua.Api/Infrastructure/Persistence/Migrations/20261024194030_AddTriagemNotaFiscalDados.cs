using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemNotaFiscalDados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DataEmissaoNotaFiscal",
                table: "triagens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroNotaFiscal",
                table: "triagens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerieNotaFiscal",
                table: "triagens",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VisionNotaFiscalDataEmissao",
                table: "triagem_channel_media",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisionNotaFiscalNumero",
                table: "triagem_channel_media",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisionNotaFiscalSerie",
                table: "triagem_channel_media",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataEmissaoNotaFiscal",
                table: "triagens");

            migrationBuilder.DropColumn(
                name: "NumeroNotaFiscal",
                table: "triagens");

            migrationBuilder.DropColumn(
                name: "SerieNotaFiscal",
                table: "triagens");

            migrationBuilder.DropColumn(
                name: "VisionNotaFiscalDataEmissao",
                table: "triagem_channel_media");

            migrationBuilder.DropColumn(
                name: "VisionNotaFiscalNumero",
                table: "triagem_channel_media");

            migrationBuilder.DropColumn(
                name: "VisionNotaFiscalSerie",
                table: "triagem_channel_media");
        }
    }
}
