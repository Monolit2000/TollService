using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MovePaymentMethodFromTollPriceToToll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethod_App",
                table: "TollPrices");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_Cash",
                table: "TollPrices");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_NoCard",
                table: "TollPrices");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_NoPlate",
                table: "TollPrices");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_Tag",
                table: "TollPrices");

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_App",
                table: "Tolls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_Cash",
                table: "Tolls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_NoCard",
                table: "Tolls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_NoPlate",
                table: "Tolls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_Tag",
                table: "Tolls",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethod_App",
                table: "Tolls");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_Cash",
                table: "Tolls");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_NoCard",
                table: "Tolls");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_NoPlate",
                table: "Tolls");

            migrationBuilder.DropColumn(
                name: "PaymentMethod_Tag",
                table: "Tolls");

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_App",
                table: "TollPrices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_Cash",
                table: "TollPrices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_NoCard",
                table: "TollPrices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_NoPlate",
                table: "TollPrices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentMethod_Tag",
                table: "TollPrices",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
