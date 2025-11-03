using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class asdsadasdasd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tolls_Roads_RoadId",
                table: "Tolls");

            migrationBuilder.AlterColumn<Guid>(
                name: "RoadId",
                table: "Tolls",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_Tolls_Roads_RoadId",
                table: "Tolls",
                column: "RoadId",
                principalTable: "Roads",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tolls_Roads_RoadId",
                table: "Tolls");

            migrationBuilder.AlterColumn<Guid>(
                name: "RoadId",
                table: "Tolls",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tolls_Roads_RoadId",
                table: "Tolls",
                column: "RoadId",
                principalTable: "Roads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
