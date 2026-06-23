using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class RenameChapterParentSeriesId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Chapters_Series_ParentMangaId",
                table: "Chapters");

            migrationBuilder.RenameColumn(
                name: "ParentMangaId",
                table: "Chapters",
                newName: "ParentSeriesId");

            migrationBuilder.RenameIndex(
                name: "IX_Chapters_ParentMangaId",
                table: "Chapters",
                newName: "IX_Chapters_ParentSeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_Chapters_Series_ParentSeriesId",
                table: "Chapters",
                column: "ParentSeriesId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Chapters_Series_ParentSeriesId",
                table: "Chapters");

            migrationBuilder.RenameColumn(
                name: "ParentSeriesId",
                table: "Chapters",
                newName: "ParentMangaId");

            migrationBuilder.RenameIndex(
                name: "IX_Chapters_ParentSeriesId",
                table: "Chapters",
                newName: "IX_Chapters_ParentMangaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Chapters_Series_ParentMangaId",
                table: "Chapters",
                column: "ParentMangaId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
