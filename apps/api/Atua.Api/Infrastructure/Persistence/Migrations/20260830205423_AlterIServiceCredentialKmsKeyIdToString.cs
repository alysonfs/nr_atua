using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlterIServiceCredentialKmsKeyIdToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Altera a coluna KmsKeyId de uuid para text.
            // Registros existentes (v1, placeholder "00000000-…-0001") são
            // convertidos para string via CAST implícito do PostgreSQL.
            // Não há dados de produção — operação segura.
            migrationBuilder.AlterColumn<string>(
                name: "KmsKeyId",
                table: "IServiceCredentials",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ATENÇÃO — RISCO DE ROLLBACK:
            // Este Down() converte a coluna KmsKeyId de text para uuid via CAST.
            // Registros criados com AlgorithmVersion=1 (placeholder) têm KmsKeyId
            // com valor "local-v1" (string não-UUID), o que já impede o rollback.
            // Registros com AlgorithmVersion=2 (KMS real) têm KmsKeyId com ARN
            // (ex.: "arn:aws:kms:..."), que também não é um UUID válido.
            // Portanto: este rollback SÓ é seguro ANTES de qualquer inserção de
            // credenciais. Se já houver registros na tabela IServiceCredentials,
            // o PostgreSQL lançará erro de conversão ("invalid input syntax for
            // type uuid"). Faça um DELETE ou truncate antes de aplicar o Down().
            migrationBuilder.AlterColumn<Guid>(
                name: "KmsKeyId",
                table: "IServiceCredentials",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
