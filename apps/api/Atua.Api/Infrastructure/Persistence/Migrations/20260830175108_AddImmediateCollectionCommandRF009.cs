using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImmediateCollectionCommandRF009 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimExpiresAtUtc",
                table: "immediate_collection_commands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "immediate_collection_commands",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_immediate_collection_commands_Status_ClaimExpiresAtUtc",
                table: "immediate_collection_commands",
                columns: new[] { "Status", "ClaimExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_immediate_collection_commands_Status_ClaimExpiresAtUtc",
                table: "immediate_collection_commands");

            migrationBuilder.DropColumn(
                name: "ClaimExpiresAtUtc",
                table: "immediate_collection_commands");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "immediate_collection_commands");
        }
    }
}
