using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SafetyStock",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SafetyStock = table.Column<int>(type: "int", nullable: false),
                    AllocationPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockRules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeductAcrossChannels = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockRules_TenantId",
                table: "StockRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockRules_TenantId_Channel",
                table: "StockRules",
                columns: new[] { "TenantId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockSettings_TenantId",
                table: "StockSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockRules");

            migrationBuilder.DropTable(
                name: "StockSettings");

            migrationBuilder.DropColumn(
                name: "SafetyStock",
                table: "Products");
        }
    }
}
