using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class AddPostActivitySeenAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActivitySeenAt",
                table: "Posts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Feedback that already exists counts as seen — only activity from now on is "new".
            migrationBuilder.Sql("UPDATE \"Posts\" SET \"ActivitySeenAt\" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivitySeenAt",
                table: "Posts");
        }
    }
}
