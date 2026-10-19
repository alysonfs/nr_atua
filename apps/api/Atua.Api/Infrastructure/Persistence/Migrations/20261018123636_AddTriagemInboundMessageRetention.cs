using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemInboundMessageRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAtUtc",
                table: "triagem_channel_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE triagem_channel_messages " +
                "SET \"ExpiresAtUtc\" = " +
                "((\"ReceivedAtUtc\" AT TIME ZONE 'UTC') + interval '5 years') AT TIME ZONE 'UTC' " +
                "WHERE \"ExpiresAtUtc\" IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAtUtc",
                table: "triagem_channel_messages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_messages_ExpiresAtUtc",
                table: "triagem_channel_messages",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_triagem_channel_messages_ExpiresAtUtc",
                table: "triagem_channel_messages");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "triagem_channel_messages");
        }
    }
}
