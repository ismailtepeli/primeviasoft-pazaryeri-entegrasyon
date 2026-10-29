using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncJobAffectedCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AffectedCount",
                table: "SyncJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AffectedCount",
                table: "SyncJobs");
        }
    }
}
