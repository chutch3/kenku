using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class ChapterSourceIdKeyIncludesChapter : Migration
    {
        // The chapter source-id key now folds in the chapter (ObjId) on top of (connector, id-on-site) so a
        // connector that reuses a bare id-on-site across chapters/series no longer collides on
        // PK_ChapterSourceIds. The key is prefix + MD5(concatenated identifiers) (see TokenGen), so the
        // re-key is a pure SQL recompute in place — no row is dropped, and UseForDownload and every other
        // column is kept. Nothing FKs to ChapterSourceIds.Key, so updating the PK in place is safe.
        internal const string RekeySql =
            """UPDATE "ChapterSourceIds" SET "Key" = 'SourceId`1-' || md5("SeriesSourceName" || "IdOnConnectorSite" || "ObjId");""";

        // Reverse: drop the chapter back out of the key, restoring the pre-migration (connector, id) form.
        internal const string RevertSql =
            """UPDATE "ChapterSourceIds" SET "Key" = 'SourceId`1-' || md5("SeriesSourceName" || "IdOnConnectorSite");""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(RekeySql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(RevertSql);
    }
}
