using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Actions
{
    /// <inheritdoc />
    public partial class RenameActionsMangaIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MangaId",
                table: "Actions",
                newName: "SeriesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SeriesId",
                table: "Actions",
                newName: "MangaId");
        }
    }
}
