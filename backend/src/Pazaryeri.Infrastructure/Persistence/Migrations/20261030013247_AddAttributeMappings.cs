using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttributeMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttributeMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExternalAttributeId = table.Column<long>(type: "bigint", nullable: false),
                    SourceAttribute = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FixedValueId = table.Column<long>(type: "bigint", nullable: true),
                    FixedCustomValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributeMappings_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttributeMappings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttributeValueMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttributeMappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExternalValueId = table.Column<long>(type: "bigint", nullable: true),
                    CustomValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeValueMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttributeValueMappings_AttributeMappings_AttributeMappingId",
                        column: x => x.AttributeMappingId,
                        principalTable: "AttributeMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttributeValueMappings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttributeMappings_CategoryId_Channel_ExternalAttributeId",
                table: "AttributeMappings",
                columns: new[] { "CategoryId", "Channel", "ExternalAttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttributeMappings_TenantId",
                table: "AttributeMappings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeValueMappings_AttributeMappingId_SourceValue",
                table: "AttributeValueMappings",
                columns: new[] { "AttributeMappingId", "SourceValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttributeValueMappings_TenantId",
                table: "AttributeValueMappings",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttributeValueMappings");

            migrationBuilder.DropTable(
                name: "AttributeMappings");
        }
    }
}
