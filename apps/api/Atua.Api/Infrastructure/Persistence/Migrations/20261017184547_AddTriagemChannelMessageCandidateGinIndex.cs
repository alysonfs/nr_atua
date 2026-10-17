using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagemChannelMessageCandidateGinIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_triagem_channel_messages_CandidateTriagemIds",
                table: "triagem_channel_messages",
                column: "CandidateTriagemIds")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_triagem_channel_messages_CandidateTriagemIds",
                table: "triagem_channel_messages");
        }
    }
}
