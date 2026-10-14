using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemTriggerOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "triagem_trigger_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderProviderId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triagem_trigger_outbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_triagem_trigger_outbox_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_triagem_trigger_outbox_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_triagem_trigger_outbox_claim_due",
                table: "triagem_trigger_outbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_triagem_trigger_outbox_claim_lease",
                table: "triagem_trigger_outbox",
                column: "ClaimExpiresAtUtc",
                filter: "\"Status\" = 'Processing'");

            migrationBuilder.CreateIndex(
                name: "IX_triagem_trigger_outbox_IntegrationId",
                table: "triagem_trigger_outbox",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_triagem_trigger_outbox_TenantId",
                table: "triagem_trigger_outbox",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "ix_triagem_trigger_outbox_work_order_id",
                table: "triagem_trigger_outbox",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "uq_triagem_trigger_outbox_tenant_integration_workorder",
                table: "triagem_trigger_outbox",
                columns: new[] { "TenantId", "IntegrationId", "WorkOrderProviderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "triagem_trigger_outbox");
        }
    }
}
