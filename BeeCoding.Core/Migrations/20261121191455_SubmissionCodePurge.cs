using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionCodePurge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CodePurgedAt",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BoardCodeRetentionDays",
                table: "PlatformRuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PracticeCodeRetentionDays",
                table: "PlatformRuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CodePurgedAt",
                table: "BankSubmissions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodePurgedAt",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "BoardCodeRetentionDays",
                table: "PlatformRuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PracticeCodeRetentionDays",
                table: "PlatformRuntimeSettings");

            migrationBuilder.DropColumn(
                name: "CodePurgedAt",
                table: "BankSubmissions");
        }
    }
}
