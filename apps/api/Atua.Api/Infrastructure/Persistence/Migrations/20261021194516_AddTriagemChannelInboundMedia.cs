using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemChannelInboundMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "triagem_channel_media",
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
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MediaKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsVoiceNote = table.Column<bool>(type: "boolean", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Caption = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TriagemId = table.Column<Guid>(type: "uuid", nullable: true),
                    CandidateTriagemIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triagem_channel_media", x => x.Id);
                    table.ForeignKey(
                        name: "FK_triagem_channel_media_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_triagem_channel_media_triagens_TriagemId",
                        column: x => x.TriagemId,
                        principalTable: "triagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_media_CandidateTriagemIds",
                table: "triagem_channel_media",
                column: "CandidateTriagemIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_media_ExpiresAtUtc",
                table: "triagem_channel_media",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_media_IntegrationId",
                table: "triagem_channel_media",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_media_TenantId_IntegrationId_SenderPhone",
                table: "triagem_channel_media",
                columns: new[] { "TenantId", "IntegrationId", "SenderPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_media_TriagemId",
                table: "triagem_channel_media",
                column: "TriagemId");

            migrationBuilder.CreateIndex(
                name: "uq_triagem_channel_media_tenant_idempotency",
                table: "triagem_channel_media",
                columns: new[] { "TenantId", "ChannelIdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "triagem_channel_media");
        }
    }
}
