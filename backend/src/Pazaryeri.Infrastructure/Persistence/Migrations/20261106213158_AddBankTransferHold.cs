using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pazaryeri.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankTransferHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BankTransferHoldHours",
                table: "B2CSettings",
                type: "int",
                nullable: false,
                defaultValue: 48);

            // Süresiz bekleyen mevcut havale siparişlerine de süre ver (şimdiden 48 saat); süre dolum işi bunları iptal edip stoğu geri ekler.
            migrationBuilder.Sql("UPDATE Orders SET PaymentExpiresAt = DATEADD(hour, 48, SYSUTCDATETIME()) WHERE Channel = 'B2C' AND PaymentMethod = 'BankTransfer' AND PaymentStatus = 'Pending' AND Status = 'New' AND PaymentExpiresAt IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankTransferHoldHours",
                table: "B2CSettings");
        }
    }
}
