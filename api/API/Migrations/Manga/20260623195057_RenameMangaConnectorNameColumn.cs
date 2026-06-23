using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class RenameMangaConnectorNameColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MangaConnectorName",
                table: "SeriesSourceIds",
                newName: "SeriesSourceName");

            migrationBuilder.RenameColumn(
                name: "MangaConnectorName",
                table: "ChapterSourceIds",
                newName: "SeriesSourceName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SeriesSourceName",
                table: "SeriesSourceIds",
                newName: "MangaConnectorName");

            migrationBuilder.RenameColumn(
                name: "SeriesSourceName",
                table: "ChapterSourceIds",
                newName: "MangaConnectorName");
        }
    }
}
