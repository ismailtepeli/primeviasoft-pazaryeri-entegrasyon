using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDealerPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DealerPasswordResets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DealerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DealerPasswordResets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DealerPasswordResets_DealerUsers_DealerUserId",
                        column: x => x.DealerUserId,
                        principalTable: "DealerUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DealerPasswordResets_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DealerPasswordResets_DealerUserId",
                table: "DealerPasswordResets",
                column: "DealerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DealerPasswordResets_TenantId",
                table: "DealerPasswordResets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DealerPasswordResets_TenantId_DealerUserId",
                table: "DealerPasswordResets",
                columns: new[] { "TenantId", "DealerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DealerPasswordResets_TokenHash",
                table: "DealerPasswordResets",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DealerPasswordResets");
        }
    }
}
