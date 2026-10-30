using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddB2BVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "B2BVisible",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "B2BHidden",
                table: "Categories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "B2BSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockDisplay = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LowStockThreshold = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_B2BSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_B2BSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroupVisibilityRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupVisibilityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupVisibilityRules_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupVisibilityRules_DealerGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "DealerGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupVisibilityRules_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupVisibilityRules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_B2BSettings_TenantId",
                table: "B2BSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupVisibilityRules_CategoryId",
                table: "GroupVisibilityRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupVisibilityRules_GroupId",
                table: "GroupVisibilityRules",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupVisibilityRules_ProductId",
                table: "GroupVisibilityRules",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupVisibilityRules_TenantId",
                table: "GroupVisibilityRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupVisibilityRules_TenantId_GroupId_ProductId_CategoryId",
                table: "GroupVisibilityRules",
                columns: new[] { "TenantId", "GroupId", "ProductId", "CategoryId" },
                unique: true,
                filter: "[ProductId] IS NOT NULL AND [CategoryId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "B2BSettings");

            migrationBuilder.DropTable(
                name: "GroupVisibilityRules");

            migrationBuilder.DropColumn(
                name: "B2BVisible",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "B2BHidden",
                table: "Categories");
        }
    }
}
