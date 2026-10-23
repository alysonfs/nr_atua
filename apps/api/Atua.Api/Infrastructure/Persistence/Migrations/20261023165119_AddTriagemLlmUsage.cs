using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemLlmUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "triagem_llm_usage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriagemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triagem_llm_usage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_triagem_llm_usage_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_triagem_llm_usage_triagens_TriagemId",
                        column: x => x.TriagemId,
                        principalTable: "triagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_triagem_llm_usage_TenantId_CreatedAtUtc",
                table: "triagem_llm_usage",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_triagem_llm_usage_TriagemId",
                table: "triagem_llm_usage",
                column: "TriagemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "triagem_llm_usage");
        }
    }
}
