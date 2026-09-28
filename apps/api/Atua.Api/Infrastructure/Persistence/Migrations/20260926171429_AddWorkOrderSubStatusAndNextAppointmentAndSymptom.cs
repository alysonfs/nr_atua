using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderSubStatusAndNextAppointmentAndSymptom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAppointmentAt",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubStatus",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SymptomRaw",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAppointmentAt",
                table: "work_order_histories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubStatus",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SymptomRaw",
                table: "work_order_histories",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextAppointmentAt",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "SubStatus",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "SymptomRaw",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "NextAppointmentAt",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "SubStatus",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "SymptomRaw",
                table: "work_order_histories");
        }
    }
}
