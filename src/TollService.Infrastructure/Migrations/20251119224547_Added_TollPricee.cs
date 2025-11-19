using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Added_TollPricee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "IPass",
                table: "Tolls",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "IPassOvernight",
                table: "Tolls",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PayOnline",
                table: "Tolls",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PayOnlineOvernight",
                table: "Tolls",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IPass",
                table: "Tolls");

            migrationBuilder.DropColumn(
                name: "IPassOvernight",
                table: "Tolls");

            migrationBuilder.DropColumn(
                name: "PayOnline",
                table: "Tolls");

            migrationBuilder.DropColumn(
                name: "PayOnlineOvernight",
                table: "Tolls");
        }
    }
}
