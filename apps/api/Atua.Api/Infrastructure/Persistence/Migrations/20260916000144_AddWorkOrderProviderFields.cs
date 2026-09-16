using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atua.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderProviderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryId",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CityName",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryName",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerCpf",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerType",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdCode",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductBrand",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductCategoryCode",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductCode",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductModel",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductStatus",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderCreatedAt",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderUpdatedAt",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateName",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Symptom",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "work_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryId",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CityName",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryName",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerCpf",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerType",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdCode",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductBrand",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductCategoryCode",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductCode",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductModel",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductStatus",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderCreatedAt",
                table: "work_order_histories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderUpdatedAt",
                table: "work_order_histories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateName",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Symptom",
                table: "work_order_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "work_order_histories",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CityName",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CountryName",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CustomerCpf",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CustomerType",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "PdCode",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ProductBrand",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ProductCategoryCode",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ProductCode",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ProductModel",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ProductStatus",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ProviderCreatedAt",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ProviderUpdatedAt",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "StateName",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "Symptom",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "CityName",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "CountryName",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "CustomerCpf",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "CustomerType",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "PdCode",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ProductBrand",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ProductCategoryCode",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ProductCode",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ProductModel",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ProductStatus",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ProviderCreatedAt",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ProviderUpdatedAt",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "StateName",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "Symptom",
                table: "work_order_histories");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "work_order_histories");
        }
    }
}
