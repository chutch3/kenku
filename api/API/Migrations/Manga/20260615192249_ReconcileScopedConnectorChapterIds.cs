using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <summary>
    /// One-time reconciliation for connector chapter-source ids that were rescoped (e.g. ComicHubFree
    /// "issue-N" → "{slug}/issue-N"): the rescoped id was added alongside the old one instead of
    /// replacing it, leaving two source rows per chapter. Removes the stale predecessor — generically,
    /// by id shape, never by series or connector name — wherever another source for the SAME chapter and
    /// connector has an id that is exactly "{scope}/" + the stale id. Distinct re-uploads (e.g. two
    /// unrelated WeebCentral chapter ULIDs for one number) are NOT a prefix of each other, so they are
    /// left intact for the per-upload chooser.
    /// </summary>
    public partial class ReconcileScopedConnectorChapterIds : Migration
    {
        internal const string DedupSql = @"
DELETE FROM ""MangaConnectorToChapter"" AS stale
USING ""MangaConnectorToChapter"" AS current
WHERE current.""ObjId"" = stale.""ObjId""
  AND current.""MangaConnectorName"" = stale.""MangaConnectorName""
  AND current.""IdOnConnectorSite"" <> stale.""IdOnConnectorSite""
  AND right(current.""IdOnConnectorSite"", length(stale.""IdOnConnectorSite"") + 1) = '/' || stale.""IdOnConnectorSite"";";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DedupSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: the stale duplicate rows carried no information the surviving row lacks.
        }
    }
}
