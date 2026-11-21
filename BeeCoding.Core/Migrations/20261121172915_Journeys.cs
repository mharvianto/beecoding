using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class Journeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Journeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Published = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JourneySkips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    BankProblemId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneySkips", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JourneyWorlds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JourneyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    UnlockAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    BossBankProblemId = table.Column<int>(type: "INTEGER", nullable: true),
                    BossXp = table.Column<int>(type: "INTEGER", nullable: false),
                    BadgeName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyWorlds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JourneyWorlds_Journeys_JourneyId",
                        column: x => x.JourneyId,
                        principalTable: "Journeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JourneyItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorldId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    BankProblemId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JourneyItems_JourneyWorlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "JourneyWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JourneyItems_WorldId",
                table: "JourneyItems",
                column: "WorldId");

            migrationBuilder.CreateIndex(
                name: "IX_JourneySkips_UserId_BankProblemId",
                table: "JourneySkips",
                columns: new[] { "UserId", "BankProblemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JourneyWorlds_JourneyId",
                table: "JourneyWorlds",
                column: "JourneyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JourneyItems");

            migrationBuilder.DropTable(
                name: "JourneySkips");

            migrationBuilder.DropTable(
                name: "JourneyWorlds");

            migrationBuilder.DropTable(
                name: "Journeys");
        }
    }
}
