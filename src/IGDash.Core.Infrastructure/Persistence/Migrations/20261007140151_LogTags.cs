using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IGDash.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LogTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "Tags",
                table: "LogEntries",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Tags",
                table: "LogEntries",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LogEntries_Tags",
                table: "LogEntries");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "LogEntries");
        }
    }
}
