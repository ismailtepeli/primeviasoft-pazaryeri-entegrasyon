using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Changed = table.Column<int>(type: "int", nullable: false),
                    Removed = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogSyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryExternalId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    AllowCustom = table.Column<bool>(type: "bit", nullable: false),
                    Varianter = table.Column<bool>(type: "bit", nullable: false),
                    Slicer = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAttributes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelAttributeValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryExternalId = table.Column<long>(type: "bigint", nullable: false),
                    AttributeExternalId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAttributeValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelBrands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelBrands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<long>(type: "bigint", nullable: false),
                    ParentExternalId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsLeaf = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogSyncRuns_Channel_Kind_FinishedAt",
                table: "CatalogSyncRuns",
                columns: new[] { "Channel", "Kind", "FinishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAttributes_Channel_CategoryExternalId_ExternalId",
                table: "ChannelAttributes",
                columns: new[] { "Channel", "CategoryExternalId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAttributeValues_Channel_CategoryExternalId_AttributeExternalId_ExternalId",
                table: "ChannelAttributeValues",
                columns: new[] { "Channel", "CategoryExternalId", "AttributeExternalId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBrands_Channel_ExternalId",
                table: "ChannelBrands",
                columns: new[] { "Channel", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBrands_Channel_Name",
                table: "ChannelBrands",
                columns: new[] { "Channel", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCategories_Channel_ExternalId",
                table: "ChannelCategories",
                columns: new[] { "Channel", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCategories_Channel_ParentExternalId",
                table: "ChannelCategories",
                columns: new[] { "Channel", "ParentExternalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogSyncRuns");

            migrationBuilder.DropTable(
                name: "ChannelAttributes");

            migrationBuilder.DropTable(
                name: "ChannelAttributeValues");

            migrationBuilder.DropTable(
                name: "ChannelBrands");

            migrationBuilder.DropTable(
                name: "ChannelCategories");
        }
    }
}
