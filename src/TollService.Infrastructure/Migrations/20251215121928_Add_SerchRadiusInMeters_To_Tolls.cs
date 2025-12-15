using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_SerchRadiusInMeters_To_Tolls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SerchRadiusInMeters",
                table: "Tolls",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SerchRadiusInMeters",
                table: "Tolls");
        }
    }
}
