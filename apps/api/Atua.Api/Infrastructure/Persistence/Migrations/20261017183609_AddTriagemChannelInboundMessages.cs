using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemChannelInboundMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "triagem_channel_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChannelIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConversationKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FromMe = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TriagemId = table.Column<Guid>(type: "uuid", nullable: true),
                    CandidateTriagemIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triagem_channel_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_triagem_channel_messages_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_triagem_channel_messages_triagens_TriagemId",
                        column: x => x.TriagemId,
                        principalTable: "triagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_messages_IntegrationId",
                table: "triagem_channel_messages",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_messages_TenantId_IntegrationId_SenderPhone",
                table: "triagem_channel_messages",
                columns: new[] { "TenantId", "IntegrationId", "SenderPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_messages_TriagemId",
                table: "triagem_channel_messages",
                column: "TriagemId");

            migrationBuilder.CreateIndex(
                name: "uq_triagem_channel_messages_tenant_idempotency",
                table: "triagem_channel_messages",
                columns: new[] { "TenantId", "ChannelIdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "triagem_channel_messages");
        }
    }
}
