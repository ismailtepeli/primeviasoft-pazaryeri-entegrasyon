using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinOrderQuantity",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PackSize",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "PriceLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DealerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    PricesIncludeVat = table.Column<bool>(type: "bit", nullable: false),
                    ShowVatIncluded = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceLists_DealerGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "DealerGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceLists_Dealers_DealerId",
                        column: x => x.DealerId,
                        principalTable: "Dealers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceLists_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceListItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceListItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceListItems_PriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "PriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceListItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceListItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceListItems_PriceListId_ProductId",
                table: "PriceListItems",
                columns: new[] { "PriceListId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceListItems_ProductId",
                table: "PriceListItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceListItems_TenantId",
                table: "PriceListItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceLists_DealerId",
                table: "PriceLists",
                column: "DealerId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceLists_GroupId",
                table: "PriceLists",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceLists_TenantId",
                table: "PriceLists",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceLists_TenantId_DealerId",
                table: "PriceLists",
                columns: new[] { "TenantId", "DealerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceLists_TenantId_GroupId",
                table: "PriceLists",
                columns: new[] { "TenantId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceLists_TenantId_Name",
                table: "PriceLists",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceListItems");

            migrationBuilder.DropTable(
                name: "PriceLists");

            migrationBuilder.DropColumn(
                name: "MinOrderQuantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackSize",
                table: "Products");
        }
    }
}
