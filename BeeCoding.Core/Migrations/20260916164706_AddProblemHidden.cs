using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class AddProblemHidden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Hidden",
                table: "Problems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Hidden",
                table: "Problems");
        }
    }
}
