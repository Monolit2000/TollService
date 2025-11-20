using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Added_Number : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Tolls",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Number",
                table: "Tolls");
        }
    }
}
