using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Jobs
{
    /// <inheritdoc />
    public partial class AddJobFailureKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailureKind",
                table: "JobQueue",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureKind",
                table: "JobQueue");
        }
    }
}
