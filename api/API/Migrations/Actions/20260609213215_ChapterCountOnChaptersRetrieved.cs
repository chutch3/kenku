using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Actions
{
    /// <inheritdoc />
    public partial class ChapterCountOnChaptersRetrieved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChapterCount",
                table: "Actions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChapterCount",
                table: "Actions");
        }
    }
}
