using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationTokensAndServiceCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "auth_sessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "auth_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auth_refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_refresh_tokens_auth_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "auth_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_credentials_integration_providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "integration_providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_credentials_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_service_credentials_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_refresh_tokens_FamilyId",
                table: "auth_refresh_tokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_refresh_tokens_SessionId",
                table: "auth_refresh_tokens",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_refresh_tokens_TokenHash",
                table: "auth_refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_credentials_IntegrationId",
                table: "service_credentials",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_service_credentials_ProviderId",
                table: "service_credentials",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_service_credentials_TenantId",
                table: "service_credentials",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_service_credentials_TokenHash",
                table: "service_credentials",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_refresh_tokens");

            migrationBuilder.DropTable(
                name: "service_credentials");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "auth_sessions");
        }
    }
}
