using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Index_To_FromId_ToId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CalculatePrices_FromId",
                table: "CalculatePrices");

            migrationBuilder.CreateIndex(
                name: "IX_CalculatePrices_FromId_ToId",
                table: "CalculatePrices",
                columns: new[] { "FromId", "ToId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CalculatePrices_FromId_ToId",
                table: "CalculatePrices");

            migrationBuilder.CreateIndex(
                name: "IX_CalculatePrices_FromId",
                table: "CalculatePrices",
                column: "FromId");
        }
    }
}
