using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class RenameMangasTableToSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AltTitle_Mangas_SeriesKey",
                table: "AltTitle");

            migrationBuilder.DropForeignKey(
                name: "FK_AuthorToManga_Mangas_MangaIds",
                table: "AuthorToManga");

            migrationBuilder.DropForeignKey(
                name: "FK_Chapters_Mangas_ParentMangaId",
                table: "Chapters");

            migrationBuilder.DropForeignKey(
                name: "FK_Link_Mangas_SeriesKey",
                table: "Link");

            migrationBuilder.DropForeignKey(
                name: "FK_MangaConnectorToManga_Mangas_ObjId",
                table: "MangaConnectorToManga");

            migrationBuilder.DropForeignKey(
                name: "FK_Mangas_FileLibraries_LibraryId",
                table: "Mangas");

            migrationBuilder.DropForeignKey(
                name: "FK_MetadataEntries_Mangas_SeriesKey",
                table: "MetadataEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_MetadataSources_Mangas_MangaId",
                table: "MetadataSources");

            migrationBuilder.DropForeignKey(
                name: "FK_SeriesTagToSeries_Mangas_MangaIds",
                table: "SeriesTagToSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_VolumeMetadata_Mangas_MangaId",
                table: "VolumeMetadata");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Mangas",
                table: "Mangas");

            migrationBuilder.RenameTable(
                name: "Mangas",
                newName: "Series");

            migrationBuilder.RenameIndex(
                name: "IX_Mangas_LibraryId",
                table: "Series",
                newName: "IX_Series_LibraryId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Series",
                table: "Series",
                column: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_AltTitle_Series_SeriesKey",
                table: "AltTitle",
                column: "SeriesKey",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AuthorToManga_Series_MangaIds",
                table: "AuthorToManga",
                column: "MangaIds",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Chapters_Series_ParentMangaId",
                table: "Chapters",
                column: "ParentMangaId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Link_Series_SeriesKey",
                table: "Link",
                column: "SeriesKey",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MangaConnectorToManga_Series_ObjId",
                table: "MangaConnectorToManga",
                column: "ObjId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MetadataEntries_Series_SeriesKey",
                table: "MetadataEntries",
                column: "SeriesKey",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MetadataSources_Series_MangaId",
                table: "MetadataSources",
                column: "MangaId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_FileLibraries_LibraryId",
                table: "Series",
                column: "LibraryId",
                principalTable: "FileLibraries",
                principalColumn: "Key",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SeriesTagToSeries_Series_MangaIds",
                table: "SeriesTagToSeries",
                column: "MangaIds",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VolumeMetadata_Series_MangaId",
                table: "VolumeMetadata",
                column: "MangaId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AltTitle_Series_SeriesKey",
                table: "AltTitle");

            migrationBuilder.DropForeignKey(
                name: "FK_AuthorToManga_Series_MangaIds",
                table: "AuthorToManga");

            migrationBuilder.DropForeignKey(
                name: "FK_Chapters_Series_ParentMangaId",
                table: "Chapters");

            migrationBuilder.DropForeignKey(
                name: "FK_Link_Series_SeriesKey",
                table: "Link");

            migrationBuilder.DropForeignKey(
                name: "FK_MangaConnectorToManga_Series_ObjId",
                table: "MangaConnectorToManga");

            migrationBuilder.DropForeignKey(
                name: "FK_MetadataEntries_Series_SeriesKey",
                table: "MetadataEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_MetadataSources_Series_MangaId",
                table: "MetadataSources");

            migrationBuilder.DropForeignKey(
                name: "FK_Series_FileLibraries_LibraryId",
                table: "Series");

            migrationBuilder.DropForeignKey(
                name: "FK_SeriesTagToSeries_Series_MangaIds",
                table: "SeriesTagToSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_VolumeMetadata_Series_MangaId",
                table: "VolumeMetadata");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Series",
                table: "Series");

            migrationBuilder.RenameTable(
                name: "Series",
                newName: "Mangas");

            migrationBuilder.RenameIndex(
                name: "IX_Series_LibraryId",
                table: "Mangas",
                newName: "IX_Mangas_LibraryId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Mangas",
                table: "Mangas",
                column: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_AltTitle_Mangas_SeriesKey",
                table: "AltTitle",
                column: "SeriesKey",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AuthorToManga_Mangas_MangaIds",
                table: "AuthorToManga",
                column: "MangaIds",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Chapters_Mangas_ParentMangaId",
                table: "Chapters",
                column: "ParentMangaId",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Link_Mangas_SeriesKey",
                table: "Link",
                column: "SeriesKey",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MangaConnectorToManga_Mangas_ObjId",
                table: "MangaConnectorToManga",
                column: "ObjId",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Mangas_FileLibraries_LibraryId",
                table: "Mangas",
                column: "LibraryId",
                principalTable: "FileLibraries",
                principalColumn: "Key",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MetadataEntries_Mangas_SeriesKey",
                table: "MetadataEntries",
                column: "SeriesKey",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MetadataSources_Mangas_MangaId",
                table: "MetadataSources",
                column: "MangaId",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SeriesTagToSeries_Mangas_MangaIds",
                table: "SeriesTagToSeries",
                column: "MangaIds",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VolumeMetadata_Mangas_MangaId",
                table: "VolumeMetadata",
                column: "MangaId",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
