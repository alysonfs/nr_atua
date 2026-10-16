using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemChannelCommand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "triagem_channel_commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriagemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triagem_channel_commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_triagem_channel_commands_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_triagem_channel_commands_triagens_TriagemId",
                        column: x => x.TriagemId,
                        principalTable: "triagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_triagem_channel_commands_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_triagem_channel_commands_claim_due",
                table: "triagem_channel_commands",
                columns: new[] { "TenantId", "IntegrationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_triagem_channel_commands_claim_lease",
                table: "triagem_channel_commands",
                column: "ClaimExpiresAtUtc",
                filter: "\"Status\" = 'Claimed'");

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_commands_IntegrationId",
                table: "triagem_channel_commands",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_commands_TenantId",
                table: "triagem_channel_commands",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "ix_triagem_channel_commands_triagem_id",
                table: "triagem_channel_commands",
                column: "TriagemId");

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_commands_WorkOrderId",
                table: "triagem_channel_commands",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "uq_triagem_channel_commands_idempotency_key",
                table: "triagem_channel_commands",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "triagem_channel_commands");
        }
    }
}
