using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSetupCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SetupCompletedAt",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            // Sihirbazdan önce var olan kiracılar kurulumu tamamlanmış sayılır; yalnızca yeni kiracılar sihirbazı görür.
            migrationBuilder.Sql("UPDATE Tenants SET SetupCompletedAt = SYSUTCDATETIME()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupCompletedAt",
                table: "Tenants");
        }
    }
}
