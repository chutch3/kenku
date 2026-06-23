using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class RenameMangaIdColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MetadataEntries_Series_SeriesKey",
                table: "MetadataEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_MetadataSources_Series_MangaId",
                table: "MetadataSources");

            migrationBuilder.DropForeignKey(
                name: "FK_VolumeMetadata_Series_MangaId",
                table: "VolumeMetadata");

            migrationBuilder.DropColumn(
                name: "MangaId",
                table: "MetadataEntries");

            migrationBuilder.RenameColumn(
                name: "MangaId",
                table: "VolumeMetadata",
                newName: "SeriesId");

            migrationBuilder.RenameIndex(
                name: "IX_VolumeMetadata_MangaId",
                table: "VolumeMetadata",
                newName: "IX_VolumeMetadata_SeriesId");

            migrationBuilder.RenameColumn(
                name: "MangaId",
                table: "MetadataSources",
                newName: "SeriesId");

            migrationBuilder.RenameColumn(
                name: "SeriesKey",
                table: "MetadataEntries",
                newName: "SeriesId");

            migrationBuilder.RenameIndex(
                name: "IX_MetadataEntries_SeriesKey",
                table: "MetadataEntries",
                newName: "IX_MetadataEntries_SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_MetadataEntries_Series_SeriesId",
                table: "MetadataEntries",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MetadataSources_Series_SeriesId",
                table: "MetadataSources",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VolumeMetadata_Series_SeriesId",
                table: "VolumeMetadata",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MetadataEntries_Series_SeriesId",
                table: "MetadataEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_MetadataSources_Series_SeriesId",
                table: "MetadataSources");

            migrationBuilder.DropForeignKey(
                name: "FK_VolumeMetadata_Series_SeriesId",
                table: "VolumeMetadata");

            migrationBuilder.RenameColumn(
                name: "SeriesId",
                table: "VolumeMetadata",
                newName: "MangaId");

            migrationBuilder.RenameIndex(
                name: "IX_VolumeMetadata_SeriesId",
                table: "VolumeMetadata",
                newName: "IX_VolumeMetadata_MangaId");

            migrationBuilder.RenameColumn(
                name: "SeriesId",
                table: "MetadataSources",
                newName: "MangaId");

            migrationBuilder.RenameColumn(
                name: "SeriesId",
                table: "MetadataEntries",
                newName: "SeriesKey");

            migrationBuilder.RenameIndex(
                name: "IX_MetadataEntries_SeriesId",
                table: "MetadataEntries",
                newName: "IX_MetadataEntries_SeriesKey");

            migrationBuilder.AddColumn<string>(
                name: "MangaId",
                table: "MetadataEntries",
                type: "text",
                nullable: false,
                defaultValue: "");

            // MangaId was always a duplicate of the series key; restore it from the FK so Down is lossless.
            migrationBuilder.Sql("""UPDATE "MetadataEntries" SET "MangaId" = "SeriesKey";""");

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
                name: "FK_VolumeMetadata_Series_MangaId",
                table: "VolumeMetadata",
                column: "MangaId",
                principalTable: "Series",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
