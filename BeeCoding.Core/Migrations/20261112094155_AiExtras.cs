using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class AiExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fallbacks",
                table: "AiProviderConfigs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxTokens",
                table: "AiProviderConfigs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasoningEffort",
                table: "AiProviderConfigs",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fallbacks",
                table: "AiProviderConfigs");

            migrationBuilder.DropColumn(
                name: "MaxTokens",
                table: "AiProviderConfigs");

            migrationBuilder.DropColumn(
                name: "ReasoningEffort",
                table: "AiProviderConfigs");
        }
    }
}
