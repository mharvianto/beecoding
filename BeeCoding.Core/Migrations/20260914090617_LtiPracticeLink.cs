using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class LtiPracticeLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BankProblemId",
                table: "LtiResourceLinks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LtiResourceLinks_BankProblemId",
                table: "LtiResourceLinks",
                column: "BankProblemId");

            migrationBuilder.AddForeignKey(
                name: "FK_LtiResourceLinks_BankProblems_BankProblemId",
                table: "LtiResourceLinks",
                column: "BankProblemId",
                principalTable: "BankProblems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LtiResourceLinks_BankProblems_BankProblemId",
                table: "LtiResourceLinks");

            migrationBuilder.DropIndex(
                name: "IX_LtiResourceLinks_BankProblemId",
                table: "LtiResourceLinks");

            migrationBuilder.DropColumn(
                name: "BankProblemId",
                table: "LtiResourceLinks");
        }
    }
}
