using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemAssumidaPorOperador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssumidaEmUtc",
                table: "triagens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssumidaPorUserId",
                table: "triagens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_triagens_tenant_status_updated_at",
                table: "triagens",
                columns: new[] { "TenantId", "Status", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_triagens_tenant_status_updated_at",
                table: "triagens");

            migrationBuilder.DropColumn(
                name: "AssumidaEmUtc",
                table: "triagens");

            migrationBuilder.DropColumn(
                name: "AssumidaPorUserId",
                table: "triagens");
        }
    }
}
