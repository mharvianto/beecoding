using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class ExamSwitches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlockPaste",
                table: "ProblemGroups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NoAiHints",
                table: "ProblemGroups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BlockPaste",
                table: "Boards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NoAiHints",
                table: "Boards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Exam mode used to mean "hide peers and block paste" and people read it as "no AI" too: switch all three on where it was on.
            migrationBuilder.Sql("""UPDATE "Boards" SET "BlockPaste" = "ExamMode", "NoAiHints" = "ExamMode";""");
            migrationBuilder.Sql("""UPDATE "ProblemGroups" SET "BlockPaste" = "ExamMode", "NoAiHints" = "ExamMode";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockPaste",
                table: "ProblemGroups");

            migrationBuilder.DropColumn(
                name: "NoAiHints",
                table: "ProblemGroups");

            migrationBuilder.DropColumn(
                name: "BlockPaste",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "NoAiHints",
                table: "Boards");
        }
    }
}
