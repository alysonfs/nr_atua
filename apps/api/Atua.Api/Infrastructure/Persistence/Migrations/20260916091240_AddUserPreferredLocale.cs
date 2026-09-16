using Atua.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AtuaDbContext))]
[Migration("20260916091240_AddUserPreferredLocale")]
public sealed class AddUserPreferredLocale : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PreferredLocale",
            table: "users",
            type: "character varying(5)",
            maxLength: 5,
            nullable: false,
            defaultValue: "pt-BR");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PreferredLocale",
            table: "users");
    }
}
