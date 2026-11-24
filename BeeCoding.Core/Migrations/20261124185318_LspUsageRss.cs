using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class LspUsageRss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RssMb",
                table: "LspUsageSamples",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RssMb",
                table: "LspUsageSamples");
        }
    }
}
