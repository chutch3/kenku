using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class RenameMangaToSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AltTitle_Mangas_MangaKey",
                table: "AltTitle");

            migrationBuilder.DropForeignKey(
                name: "FK_Link_Mangas_MangaKey",
                table: "Link");

            migrationBuilder.DropForeignKey(
                name: "FK_MetadataEntries_Mangas_MangaId",
                table: "MetadataEntries");

            migrationBuilder.DropTable(
                name: "MangaTagToManga");

            migrationBuilder.DropIndex(
                name: "IX_MetadataEntries_MangaId",
                table: "MetadataEntries");

            migrationBuilder.RenameColumn(
                name: "MangaKey",
                table: "Link",
                newName: "SeriesKey");

            migrationBuilder.RenameIndex(
                name: "IX_Link_MangaKey",
                table: "Link",
                newName: "IX_Link_SeriesKey");

            migrationBuilder.RenameColumn(
                name: "MangaKey",
                table: "AltTitle",
                newName: "SeriesKey");

            migrationBuilder.RenameIndex(
                name: "IX_AltTitle_MangaKey",
                table: "AltTitle",
                newName: "IX_AltTitle_SeriesKey");

            migrationBuilder.AlterColumn<string>(
                name: "MangaId",
                table: "MetadataEntries",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AddColumn<string>(
                name: "SeriesKey",
                table: "MetadataEntries",
                type: "character varying(64)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "SeriesTagToSeries",
                columns: table => new
                {
                    MangaTagIds = table.Column<string>(type: "character varying(64)", nullable: false),
                    MangaIds = table.Column<string>(type: "character varying(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesTagToSeries", x => new { x.MangaTagIds, x.MangaIds });
                    table.ForeignKey(
                        name: "FK_SeriesTagToSeries_Mangas_MangaIds",
                        column: x => x.MangaIds,
                        principalTable: "Mangas",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesTagToSeries_Tags_MangaTagIds",
                        column: x => x.MangaTagIds,
                        principalTable: "Tags",
                        principalColumn: "Tag",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataEntries_SeriesKey",
                table: "MetadataEntries",
                column: "SeriesKey");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesTagToSeries_MangaIds",
                table: "SeriesTagToSeries",
                column: "MangaIds");

            migrationBuilder.AddForeignKey(
                name: "FK_AltTitle_Mangas_SeriesKey",
                table: "AltTitle",
                column: "SeriesKey",
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
                name: "FK_MetadataEntries_Mangas_SeriesKey",
                table: "MetadataEntries",
                column: "SeriesKey",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AltTitle_Mangas_SeriesKey",
                table: "AltTitle");

            migrationBuilder.DropForeignKey(
                name: "FK_Link_Mangas_SeriesKey",
                table: "Link");

            migrationBuilder.DropForeignKey(
                name: "FK_MetadataEntries_Mangas_SeriesKey",
                table: "MetadataEntries");

            migrationBuilder.DropTable(
                name: "SeriesTagToSeries");

            migrationBuilder.DropIndex(
                name: "IX_MetadataEntries_SeriesKey",
                table: "MetadataEntries");

            migrationBuilder.DropColumn(
                name: "SeriesKey",
                table: "MetadataEntries");

            migrationBuilder.RenameColumn(
                name: "SeriesKey",
                table: "Link",
                newName: "MangaKey");

            migrationBuilder.RenameIndex(
                name: "IX_Link_SeriesKey",
                table: "Link",
                newName: "IX_Link_MangaKey");

            migrationBuilder.RenameColumn(
                name: "SeriesKey",
                table: "AltTitle",
                newName: "MangaKey");

            migrationBuilder.RenameIndex(
                name: "IX_AltTitle_SeriesKey",
                table: "AltTitle",
                newName: "IX_AltTitle_MangaKey");

            migrationBuilder.AlterColumn<string>(
                name: "MangaId",
                table: "MetadataEntries",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "MangaTagToManga",
                columns: table => new
                {
                    MangaTagIds = table.Column<string>(type: "character varying(64)", nullable: false),
                    MangaIds = table.Column<string>(type: "character varying(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MangaTagToManga", x => new { x.MangaTagIds, x.MangaIds });
                    table.ForeignKey(
                        name: "FK_MangaTagToManga_Mangas_MangaIds",
                        column: x => x.MangaIds,
                        principalTable: "Mangas",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MangaTagToManga_Tags_MangaTagIds",
                        column: x => x.MangaTagIds,
                        principalTable: "Tags",
                        principalColumn: "Tag",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataEntries_MangaId",
                table: "MetadataEntries",
                column: "MangaId");

            migrationBuilder.CreateIndex(
                name: "IX_MangaTagToManga_MangaIds",
                table: "MangaTagToManga",
                column: "MangaIds");

            migrationBuilder.AddForeignKey(
                name: "FK_AltTitle_Mangas_MangaKey",
                table: "AltTitle",
                column: "MangaKey",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Link_Mangas_MangaKey",
                table: "Link",
                column: "MangaKey",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MetadataEntries_Mangas_MangaId",
                table: "MetadataEntries",
                column: "MangaId",
                principalTable: "Mangas",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
