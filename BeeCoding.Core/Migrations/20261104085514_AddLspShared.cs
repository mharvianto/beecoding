using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class AddLspShared : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LspShared",
                table: "PlatformRuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LspShared",
                table: "PlatformRuntimeSettings");
        }
    }
}
