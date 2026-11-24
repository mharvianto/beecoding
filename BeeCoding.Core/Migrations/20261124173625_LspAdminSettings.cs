using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class LspAdminSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LspDefaultOn",
                table: "PlatformRuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LspIdleTimeoutSeconds",
                table: "PlatformRuntimeSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LspMaxConcurrent",
                table: "PlatformRuntimeSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LspMemoryLimitMb",
                table: "PlatformRuntimeSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LspSharedMaxProcesses",
                table: "PlatformRuntimeSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LspSharedMaxSessions",
                table: "PlatformRuntimeSettings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LspDefaultOn",
                table: "PlatformRuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LspIdleTimeoutSeconds",
                table: "PlatformRuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LspMaxConcurrent",
                table: "PlatformRuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LspMemoryLimitMb",
                table: "PlatformRuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LspSharedMaxProcesses",
                table: "PlatformRuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LspSharedMaxSessions",
                table: "PlatformRuntimeSettings");
        }
    }
}
