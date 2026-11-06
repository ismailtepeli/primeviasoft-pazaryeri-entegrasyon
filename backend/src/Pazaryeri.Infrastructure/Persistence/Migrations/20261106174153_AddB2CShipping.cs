using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddB2CShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrackingUrl",
                table: "Orders",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShippingCarriers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    FlatFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TrackingUrlTemplate = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingCarriers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingCarriers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShippingRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CarrierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpToDesi = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingRates_ShippingCarriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "ShippingCarriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShippingRates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingCarriers_TenantId",
                table: "ShippingCarriers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingCarriers_TenantId_SortOrder",
                table: "ShippingCarriers",
                columns: new[] { "TenantId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRates_CarrierId",
                table: "ShippingRates",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRates_TenantId",
                table: "ShippingRates",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShippingRates");

            migrationBuilder.DropTable(
                name: "ShippingCarriers");

            migrationBuilder.DropColumn(
                name: "TrackingUrl",
                table: "Orders");
        }
    }
}
