using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_TollPrices_to_CalculatePrice_CalculatePriceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateTimeOfDay",
                table: "TollPrices");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TimeFrom",
                table: "TollPrices",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TimeTo",
                table: "TollPrices",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeFrom",
                table: "TollPrices");

            migrationBuilder.DropColumn(
                name: "TimeTo",
                table: "TollPrices");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateTimeOfDay",
                table: "TollPrices",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
