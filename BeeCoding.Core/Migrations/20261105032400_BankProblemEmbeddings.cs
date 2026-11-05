using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class BankProblemEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankProblemEmbeddings",
                columns: table => new
                {
                    BankProblemId = table.Column<int>(type: "INTEGER", nullable: false),
                    TextHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Vector = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankProblemEmbeddings", x => x.BankProblemId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankProblemEmbeddings");
        }
    }
}
