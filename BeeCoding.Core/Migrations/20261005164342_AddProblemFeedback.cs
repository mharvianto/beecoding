using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class AddProblemFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProblemLikes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    BankProblemId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProblemId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemLikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemLikes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProblemReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    BankProblemId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProblemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemReports_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProblemLikes_BankProblemId",
                table: "ProblemLikes",
                column: "BankProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemLikes_ProblemId",
                table: "ProblemLikes",
                column: "ProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemLikes_UserId_BankProblemId",
                table: "ProblemLikes",
                columns: new[] { "UserId", "BankProblemId" },
                unique: true,
                filter: "BankProblemId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemLikes_UserId_ProblemId",
                table: "ProblemLikes",
                columns: new[] { "UserId", "ProblemId" },
                unique: true,
                filter: "ProblemId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_BankProblemId",
                table: "ProblemReports",
                column: "BankProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_ProblemId",
                table: "ProblemReports",
                column: "ProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_Status_CreatedAt",
                table: "ProblemReports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_UserId",
                table: "ProblemReports",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProblemLikes");

            migrationBuilder.DropTable(
                name: "ProblemReports");
        }
    }
}
