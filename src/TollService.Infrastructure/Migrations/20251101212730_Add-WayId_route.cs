using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWayId_route : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WayId",
                table: "Roads",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WayId",
                table: "Roads");
        }
    }
}
