using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurrentCollectionIntervalToIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecurrentCollectionIntervalMinutes",
                table: "integrations",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.CreateIndex(
                name: "IX_immediate_collection_commands_IntegrationId_RequestedAtUtc",
                table: "immediate_collection_commands",
                columns: new[] { "IntegrationId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_immediate_collection_commands_IntegrationId_RequestedAtUtc",
                table: "immediate_collection_commands");

            migrationBuilder.DropColumn(
                name: "RecurrentCollectionIntervalMinutes",
                table: "integrations");
        }
    }
}
