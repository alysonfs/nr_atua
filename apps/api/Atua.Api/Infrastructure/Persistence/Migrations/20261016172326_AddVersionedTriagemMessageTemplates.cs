using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionedTriagemMessageTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RenderedContent",
                table: "triagem_channel_commands",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    template_text text := trim($template$
                    📩 Triagem da OS {{numero_os}}

                    Olá! Somos da {{nome_posto}}, assistência técnica autorizada
                    {{fabricante}}. Recebemos a ordem de serviço do produto {{produto}} e
                    precisamos realizar uma triagem antes do atendimento, para aumentar a chance
                    de resolver o problema na primeira visita.

                    Por favor, envie:
                    ✅ descrição detalhada do defeito, incluindo eventual código de erro;
                    📄 foto legível da nota fiscal;
                    🏷️ foto legível da etiqueta com modelo e número de série;
                    📷 pelo menos uma foto ou vídeo mostrando o defeito;
                    📍 endereço completo onde o produto está instalado.

                    Se possível, envie também um ponto de referência e a localização do endereço.

                    Você pode enviar as informações em mensagens separadas. Quanto mais detalhes
                    recebermos, mais rápido poderemos analisar o atendimento.

                    Se as informações obrigatórias não forem enviadas em até 48 horas, a análise
                    poderá atrasar e nossa equipe poderá entrar em contato para continuar o
                    atendimento. A ordem de serviço não será cancelada automaticamente por falta
                    dessas informações.
                    $template$);
                BEGIN
                    UPDATE triagem_channel_commands AS command
                    SET "RenderedContent" = replace(
                        replace(
                            replace(
                                replace(
                                    template_text,
                                    '{{nome_posto}}', tenant."Name"),
                                '{{fabricante}}', provider."Manufacturer"),
                            '{{numero_os}}',
                            COALESCE(NULLIF(work_order."WorkOrderProviderNo", ''),
                                work_order."WorkOrderProviderId")),
                        '{{produto}}',
                        COALESCE(
                            NULLIF(BTRIM(CONCAT_WS(' ',
                                NULLIF(work_order."ProductBrand", ''),
                                NULLIF(work_order."ProductModel", ''))), ''),
                            'seu produto'))
                    FROM work_orders AS work_order
                    JOIN tenants AS tenant
                        ON tenant."Id" = work_order."TenantId"
                    JOIN integrations AS integration
                        ON integration."Id" = work_order."IntegrationId"
                    JOIN integration_providers AS provider
                        ON provider."Id" = integration."ProviderId"
                    WHERE command."WorkOrderId" = work_order."Id"
                      AND command."TenantId" = tenant."Id"
                      AND command."IntegrationId" = integration."Id";

                    IF EXISTS (
                        SELECT 1
                        FROM triagem_channel_commands
                        WHERE "RenderedContent" IS NULL
                    ) THEN
                        RAISE EXCEPTION
                            'Não foi possível renderizar comandos de canal preexistentes.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "RenderedContent",
                table: "triagem_channel_commands",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8000)",
                oldMaxLength: 8000,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "triagem_message_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triagem_message_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_triagem_message_templates_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_triagem_message_templates_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_triagem_message_templates_IntegrationId",
                table: "triagem_message_templates",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "ux_triagem_message_templates_active",
                table: "triagem_message_templates",
                columns: new[] { "TenantId", "IntegrationId", "Type" },
                unique: true,
                filter: "\"Status\" = 'Published' AND \"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ux_triagem_message_templates_draft",
                table: "triagem_message_templates",
                columns: new[] { "TenantId", "IntegrationId", "Type", "Status" },
                unique: true,
                filter: "\"Status\" = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "ux_triagem_message_templates_version",
                table: "triagem_message_templates",
                columns: new[] { "TenantId", "IntegrationId", "Type", "Version" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO triagem_message_templates
                    ("Id", "TenantId", "IntegrationId", "Type", "Version", "Content", "Status",
                     "IsActive", "CreatedAtUtc", "UpdatedAtUtc", "PublishedAtUtc", "ConcurrencyToken")
                SELECT gen_random_uuid(), i."TenantId", i."Id", 'InitialContact', 1,
                trim($template$
                📩 Triagem da OS {{numero_os}}

                Olá! Somos da {{nome_posto}}, assistência técnica autorizada
                {{fabricante}}. Recebemos a ordem de serviço do produto {{produto}} e
                precisamos realizar uma triagem antes do atendimento, para aumentar a chance
                de resolver o problema na primeira visita.

                Por favor, envie:
                ✅ descrição detalhada do defeito, incluindo eventual código de erro;
                📄 foto legível da nota fiscal;
                🏷️ foto legível da etiqueta com modelo e número de série;
                📷 pelo menos uma foto ou vídeo mostrando o defeito;
                📍 endereço completo onde o produto está instalado.

                Se possível, envie também um ponto de referência e a localização do endereço.

                Você pode enviar as informações em mensagens separadas. Quanto mais detalhes
                recebermos, mais rápido poderemos analisar o atendimento.

                Se as informações obrigatórias não forem enviadas em até 48 horas, a análise
                poderá atrasar e nossa equipe poderá entrar em contato para continuar o
                atendimento. A ordem de serviço não será cancelada automaticamente por falta
                dessas informações.
                $template$),
                'Published', TRUE, NOW(), NOW(), NOW(), gen_random_uuid()
                FROM integrations i;

                INSERT INTO triagem_message_templates
                    ("Id", "TenantId", "IntegrationId", "Type", "Version", "Content", "Status",
                     "IsActive", "CreatedAtUtc", "UpdatedAtUtc", "PublishedAtUtc", "ConcurrencyToken")
                SELECT gen_random_uuid(), i."TenantId", i."Id", 'Reminder24Hours', 1,
                trim($template$
                Olá! Para continuarmos a triagem da OS {{numero_os}}, ainda precisamos receber:
                {{itens_pendentes}}

                Você pode enviar os itens em mensagens separadas. Assim que recebermos as
                informações obrigatórias, seguiremos com a análise.
                $template$),
                'Published', TRUE, NOW(), NOW(), NOW(), gen_random_uuid()
                FROM integrations i;

                INSERT INTO triagem_message_templates
                    ("Id", "TenantId", "IntegrationId", "Type", "Version", "Content", "Status",
                     "IsActive", "CreatedAtUtc", "UpdatedAtUtc", "PublishedAtUtc", "ConcurrencyToken")
                SELECT gen_random_uuid(), i."TenantId", i."Id", 'Reminder36Hours', 1,
                trim($template$
                Olá! A triagem da OS {{numero_os}} continua incompleta. Ainda faltam:
                {{itens_pendentes}}

                O prazo de 48 horas termina em aproximadamente 12 horas. A ordem de serviço
                não será cancelada automaticamente por falta dessas informações, mas a
                ausência dos dados poderá atrasar o atendimento e nossa equipe poderá entrar
                em contato.
                $template$),
                'Published', TRUE, NOW(), NOW(), NOW(), gen_random_uuid()
                FROM integrations i;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "triagem_message_templates");

            migrationBuilder.DropColumn(
                name: "RenderedContent",
                table: "triagem_channel_commands");
        }
    }
}
