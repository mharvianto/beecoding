using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BeeCoding.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class LspUsageSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LspUsageSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Host = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Students = table.Column<int>(type: "integer", nullable: false),
                    Processes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LspUsageSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LspUsageSamples_At",
                table: "LspUsageSamples",
                column: "At");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LspUsageSamples");
        }
    }
}
