using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class NotificationPrefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotifyEmailKinds",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyOffKinds",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyEmailKinds",
                table: "PlatformRuntimeSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyOffKinds",
                table: "PlatformRuntimeSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifyEmailKinds",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyOffKinds",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyEmailKinds",
                table: "PlatformRuntimeSettings");

            migrationBuilder.DropColumn(
                name: "NotifyOffKinds",
                table: "PlatformRuntimeSettings");
        }
    }
}
