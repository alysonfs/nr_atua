using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemAttachmentVisionClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VisionClassifiedAtUtc",
                table: "triagem_channel_media",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VisionConfianca",
                table: "triagem_channel_media",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisionLegivel",
                table: "triagem_channel_media",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisionModelo",
                table: "triagem_channel_media",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisionNumeroSerie",
                table: "triagem_channel_media",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisionTipoDetectado",
                table: "triagem_channel_media",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisionClassifiedAtUtc",
                table: "triagem_channel_media");

            migrationBuilder.DropColumn(
                name: "VisionConfianca",
                table: "triagem_channel_media");

            migrationBuilder.DropColumn(
                name: "VisionLegivel",
                table: "triagem_channel_media");

            migrationBuilder.DropColumn(
                name: "VisionModelo",
                table: "triagem_channel_media");

            migrationBuilder.DropColumn(
                name: "VisionNumeroSerie",
                table: "triagem_channel_media");

            migrationBuilder.DropColumn(
                name: "VisionTipoDetectado",
                table: "triagem_channel_media");
        }
    }
}
