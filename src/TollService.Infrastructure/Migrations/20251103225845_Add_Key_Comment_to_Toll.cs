using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TollService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Key_Comment_to_Toll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tolls",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "Tolls",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "Tolls",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                table: "Tolls");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "Tolls");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tolls",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}
