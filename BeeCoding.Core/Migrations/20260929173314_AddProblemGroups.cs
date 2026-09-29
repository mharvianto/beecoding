using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class AddProblemGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "Problems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProblemGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BoardId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Hidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExamMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    OpensAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemGroups_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Problems_GroupId",
                table: "Problems",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemGroups_BoardId",
                table: "ProblemGroups",
                column: "BoardId");

            migrationBuilder.AddForeignKey(
                name: "FK_Problems_ProblemGroups_GroupId",
                table: "Problems",
                column: "GroupId",
                principalTable: "ProblemGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Problems_ProblemGroups_GroupId",
                table: "Problems");

            migrationBuilder.DropTable(
                name: "ProblemGroups");

            migrationBuilder.DropIndex(
                name: "IX_Problems_GroupId",
                table: "Problems");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Problems");
        }
    }
}
