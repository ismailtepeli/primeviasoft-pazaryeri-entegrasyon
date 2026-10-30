using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BatchPending",
                table: "SyncJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "BatchSentAt",
                table: "SyncJobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "SyncJobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VatRate",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ProductChannelData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BrandId = table.Column<long>(type: "bigint", nullable: true),
                    CategoryId = table.Column<long>(type: "bigint", nullable: true),
                    CargoCompanyId = table.Column<long>(type: "bigint", nullable: true),
                    DimensionalWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    AttributesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductChannelData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductChannelData_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductChannelData_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_BatchPending_BatchSentAt",
                table: "SyncJobs",
                columns: new[] { "BatchPending", "BatchSentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelData_ProductId_Channel",
                table: "ProductChannelData",
                columns: new[] { "ProductId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelData_TenantId",
                table: "ProductChannelData",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductChannelData");

            migrationBuilder.DropIndex(
                name: "IX_SyncJobs_BatchPending_BatchSentAt",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "BatchPending",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "BatchSentAt",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "Payload",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Products");
        }
    }
}
