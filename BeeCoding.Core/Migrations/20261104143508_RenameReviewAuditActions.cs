using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeCoding.Migrations
{
    /// <inheritdoc />
    public partial class RenameReviewAuditActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE AuditLogEntries SET Action = REPLACE(Action, 'ai-review-', 'problem-review-') WHERE Action IN ('ai-review-approve', 'ai-review-reject');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE AuditLogEntries SET Action = REPLACE(Action, 'problem-review-', 'ai-review-') WHERE Action IN ('problem-review-approve', 'problem-review-reject');");
        }
    }
}
