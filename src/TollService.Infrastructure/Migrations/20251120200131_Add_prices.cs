using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_prices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StateCalculatorId",
                table: "Tolls",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StateCalculators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StateCode = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StateCalculators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CalculatePrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StateCalculatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToId = table.Column<Guid>(type: "uuid", nullable: false),
                    Online = table.Column<double>(type: "double precision", nullable: false),
                    IPass = table.Column<double>(type: "double precision", nullable: false),
                    Cash = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculatePrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalculatePrices_StateCalculators_StateCalculatorId",
                        column: x => x.StateCalculatorId,
                        principalTable: "StateCalculators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalculatePrices_Tolls_FromId",
                        column: x => x.FromId,
                        principalTable: "Tolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalculatePrices_Tolls_ToId",
                        column: x => x.ToId,
                        principalTable: "Tolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalculatePrices_FromId",
                table: "CalculatePrices",
                column: "FromId");

            migrationBuilder.CreateIndex(
                name: "IX_CalculatePrices_StateCalculatorId",
                table: "CalculatePrices",
                column: "StateCalculatorId");

            migrationBuilder.CreateIndex(
                name: "IX_CalculatePrices_ToId",
                table: "CalculatePrices",
                column: "ToId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalculatePrices");

            migrationBuilder.DropTable(
                name: "StateCalculators");

            migrationBuilder.DropColumn(
                name: "StateCalculatorId",
                table: "Tolls");
        }
    }
}
