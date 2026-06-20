using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class AddCoverSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CoverSource",
                table: "Mangas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing covers came from a connector; mark them Connector-ranked (2) so a later provider
            // backfill (MAL/Metron) doesn't clobber them. Empty covers stay None (0), open to any source.
            migrationBuilder.Sql("UPDATE \"Mangas\" SET \"CoverSource\" = 2 WHERE \"CoverUrl\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverSource",
                table: "Mangas");
        }
    }
}
