using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNeedsDetailFetchToWorkOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DetailsFetchedAt",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsDetailFetch",
                table: "work_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_pending_detail_fetch",
                table: "work_orders",
                column: "IntegrationId",
                filter: "\"NeedsDetailFetch\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_orders_pending_detail_fetch",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "DetailsFetchedAt",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "NeedsDetailFetch",
                table: "work_orders");
        }
    }
}
