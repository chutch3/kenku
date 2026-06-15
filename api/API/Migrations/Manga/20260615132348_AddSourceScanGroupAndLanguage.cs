using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class AddSourceScanGroupAndLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "MangaConnectorToManga",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanGroup",
                table: "MangaConnectorToManga",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "MangaConnectorToChapter",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanGroup",
                table: "MangaConnectorToChapter",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "MangaConnectorToManga");

            migrationBuilder.DropColumn(
                name: "ScanGroup",
                table: "MangaConnectorToManga");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "MangaConnectorToChapter");

            migrationBuilder.DropColumn(
                name: "ScanGroup",
                table: "MangaConnectorToChapter");
        }
    }
}
