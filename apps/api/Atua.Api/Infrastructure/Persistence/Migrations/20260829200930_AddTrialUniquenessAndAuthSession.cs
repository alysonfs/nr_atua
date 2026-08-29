using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialUniquenessAndAuthSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_trial_subscriptions_UserId",
                table: "trial_subscriptions");

            migrationBuilder.CreateTable(
                name: "auth_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimeZoneOverrideId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_sessions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trial_subscriptions_UserId",
                table: "trial_subscriptions",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_sessions_UserId",
                table: "auth_sessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_sessions");

            migrationBuilder.DropIndex(
                name: "IX_trial_subscriptions_UserId",
                table: "trial_subscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_trial_subscriptions_UserId",
                table: "trial_subscriptions",
                column: "UserId");
        }
    }
}
