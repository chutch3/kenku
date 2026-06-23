using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class RenameSourceIdAndJoinTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MangaConnectorToChapter_Chapters_ObjId",
                table: "MangaConnectorToChapter");

            migrationBuilder.DropForeignKey(
                name: "FK_MangaConnectorToManga_Series_ObjId",
                table: "MangaConnectorToManga");

            // EF scaffolds a join-entity rename as Drop+Create (data-destructive); rewritten as a RenameTable.
            migrationBuilder.RenameTable(
                name: "AuthorToManga",
                newName: "AuthorToSeries");

            migrationBuilder.RenameIndex(
                name: "IX_AuthorToManga_MangaIds",
                table: "AuthorToSeries",
                newName: "IX_AuthorToSeries_MangaIds");

            migrationBuilder.Sql("""ALTER TABLE "AuthorToSeries" RENAME CONSTRAINT "PK_AuthorToManga" TO "PK_AuthorToSeries";""");
            migrationBuilder.Sql("""ALTER TABLE "AuthorToSeries" RENAME CONSTRAINT "FK_AuthorToManga_Authors_AuthorIds" TO "FK_AuthorToSeries_Authors_AuthorIds";""");
            migrationBuilder.Sql("""ALTER TABLE "AuthorToSeries" RENAME CONSTRAINT "FK_AuthorToManga_Series_MangaIds" TO "FK_AuthorToSeries_Series_MangaIds";""");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MangaConnectorToManga",
                table: "MangaConnectorToManga");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MangaConnectorToChapter",
                table: "MangaConnectorToChapter");

            migrationBuilder.RenameTable(
                name: "MangaConnectorToManga",
                newName: "SeriesSourceIds");

            migrationBuilder.RenameTable(
                name: "MangaConnectorToChapter",
                newName: "ChapterSourceIds");

            migrationBuilder.RenameIndex(
                name: "IX_MangaConnectorToManga_ObjId",
                table: "SeriesSourceIds",
                newName: "IX_SeriesSourceIds_ObjId");

            migrationBuilder.RenameIndex(
                name: "IX_MangaConnectorToChapter_ObjId",
                table: "ChapterSourceIds",
                newName: "IX_ChapterSourceIds_ObjId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SeriesSourceIds",
                table: "SeriesSourceIds",
                column: "Key");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ChapterSourceIds",
                table: "ChapterSourceIds",
                column: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_ChapterSourceIds_Chapters_ObjId",
                table: "ChapterSourceIds",
                column: "ObjId",
                principalTable: "Chapters",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SeriesSourceIds_Series_ObjId",
                table: "SeriesSourceIds",
                column: "ObjId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChapterSourceIds_Chapters_ObjId",
                table: "ChapterSourceIds");

            migrationBuilder.DropForeignKey(
                name: "FK_SeriesSourceIds_Series_ObjId",
                table: "SeriesSourceIds");

            migrationBuilder.RenameTable(
                name: "AuthorToSeries",
                newName: "AuthorToManga");

            migrationBuilder.RenameIndex(
                name: "IX_AuthorToSeries_MangaIds",
                table: "AuthorToManga",
                newName: "IX_AuthorToManga_MangaIds");

            migrationBuilder.Sql("""ALTER TABLE "AuthorToManga" RENAME CONSTRAINT "PK_AuthorToSeries" TO "PK_AuthorToManga";""");
            migrationBuilder.Sql("""ALTER TABLE "AuthorToManga" RENAME CONSTRAINT "FK_AuthorToSeries_Authors_AuthorIds" TO "FK_AuthorToManga_Authors_AuthorIds";""");
            migrationBuilder.Sql("""ALTER TABLE "AuthorToManga" RENAME CONSTRAINT "FK_AuthorToSeries_Series_MangaIds" TO "FK_AuthorToManga_Series_MangaIds";""");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SeriesSourceIds",
                table: "SeriesSourceIds");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ChapterSourceIds",
                table: "ChapterSourceIds");

            migrationBuilder.RenameTable(
                name: "SeriesSourceIds",
                newName: "MangaConnectorToManga");

            migrationBuilder.RenameTable(
                name: "ChapterSourceIds",
                newName: "MangaConnectorToChapter");

            migrationBuilder.RenameIndex(
                name: "IX_SeriesSourceIds_ObjId",
                table: "MangaConnectorToManga",
                newName: "IX_MangaConnectorToManga_ObjId");

            migrationBuilder.RenameIndex(
                name: "IX_ChapterSourceIds_ObjId",
                table: "MangaConnectorToChapter",
                newName: "IX_MangaConnectorToChapter_ObjId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MangaConnectorToManga",
                table: "MangaConnectorToManga",
                column: "Key");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MangaConnectorToChapter",
                table: "MangaConnectorToChapter",
                column: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_MangaConnectorToChapter_Chapters_ObjId",
                table: "MangaConnectorToChapter",
                column: "ObjId",
                principalTable: "Chapters",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MangaConnectorToManga_Series_ObjId",
                table: "MangaConnectorToManga",
                column: "ObjId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
