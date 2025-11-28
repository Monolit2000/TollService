using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_TollPrices_to_CalculatePrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CalculatePriceId",
                table: "TollPrices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TollPrices_CalculatePriceId",
                table: "TollPrices",
                column: "CalculatePriceId");

            migrationBuilder.AddForeignKey(
                name: "FK_TollPrices_CalculatePrices_CalculatePriceId",
                table: "TollPrices",
                column: "CalculatePriceId",
                principalTable: "CalculatePrices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TollPrices_CalculatePrices_CalculatePriceId",
                table: "TollPrices");

            migrationBuilder.DropIndex(
                name: "IX_TollPrices_CalculatePriceId",
                table: "TollPrices");

            migrationBuilder.DropColumn(
                name: "CalculatePriceId",
                table: "TollPrices");
        }
    }
}
