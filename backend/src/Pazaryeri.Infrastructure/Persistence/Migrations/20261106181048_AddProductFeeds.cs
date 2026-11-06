using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductFeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FeedId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductFeeds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    ItemPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MappingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MarkupPercent = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    PriceIncludesVat = table.Column<bool>(type: "bit", nullable: false),
                    DefaultVatRate = table.Column<int>(type: "int", nullable: false),
                    AvailableStock = table.Column<int>(type: "int", nullable: false),
                    SyncPrice = table.Column<bool>(type: "bit", nullable: false),
                    SyncStock = table.Column<bool>(type: "bit", nullable: false),
                    ZeroMissing = table.Column<bool>(type: "bit", nullable: false),
                    PublishB2C = table.Column<bool>(type: "bit", nullable: false),
                    PublishB2B = table.Column<bool>(type: "bit", nullable: false),
                    IntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RunningSince = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LastCreated = table.Column<int>(type: "int", nullable: false),
                    LastUpdated = table.Column<int>(type: "int", nullable: false),
                    LastSkipped = table.Column<int>(type: "int", nullable: false),
                    LastFailed = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastErrorsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFeeds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductFeeds_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_FeedId",
                table: "Products",
                column: "FeedId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_FeedId",
                table: "Products",
                columns: new[] { "TenantId", "FeedId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductFeeds_TenantId",
                table: "ProductFeeds",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductFeeds_FeedId",
                table: "Products",
                column: "FeedId",
                principalTable: "ProductFeeds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductFeeds_FeedId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ProductFeeds");

            migrationBuilder.DropIndex(
                name: "IX_Products_FeedId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_FeedId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "FeedId",
                table: "Products");
        }
    }
}
