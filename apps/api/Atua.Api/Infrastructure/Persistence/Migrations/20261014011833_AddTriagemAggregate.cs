using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "triagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DescricaoDefeito = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NotaFiscalLegivel = table.Column<bool>(type: "boolean", nullable: false),
                    EtiquetaModelo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EtiquetaSerie = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MidiaDefeitoTipo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    EnderecoCompleto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PontoReferencia = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Localizacao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DesviosConsecutivos = table.Column<int>(type: "integer", nullable: false),
                    WorkOrderProviderId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TentativasInvalidasPorItem = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_triagens_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_triagens_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_triagens_IntegrationId",
                table: "triagens",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_triagens_TenantId",
                table: "triagens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_triagens_WorkOrderId",
                table: "triagens",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "uq_triagens_tenant_integration_work_order_provider_id",
                table: "triagens",
                columns: new[] { "TenantId", "IntegrationId", "WorkOrderProviderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "triagens");
        }
    }
}
