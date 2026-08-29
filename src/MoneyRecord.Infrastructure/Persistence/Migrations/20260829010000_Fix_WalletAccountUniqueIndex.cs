using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Change unique index on AccountNumber to composite (WalletProviderId, AccountNumber)
    /// so same phone number can exist across different providers.
    /// </summary>
    public partial class Fix_WalletAccountUniqueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop old single-column unique index
            migrationBuilder.DropIndex(
                name: "UQ_WalletAccounts_AccountNumber",
                table: "WalletAccounts");

            // Create new composite unique index (Provider + AccountNumber)
            migrationBuilder.CreateIndex(
                name: "UQ_WalletAccounts_Provider_AccountNumber",
                table: "WalletAccounts",
                columns: new[] { "WalletProviderId", "AccountNumber" },
                unique: true,
                filter: "\"AccountNumber\" IS NOT NULL AND \"IsDeleted\" = false");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to single-column unique index
            migrationBuilder.DropIndex(
                name: "UQ_WalletAccounts_Provider_AccountNumber",
                table: "WalletAccounts");

            migrationBuilder.CreateIndex(
                name: "UQ_WalletAccounts_AccountNumber",
                table: "WalletAccounts",
                column: "AccountNumber",
                unique: true,
                filter: "\"AccountNumber\" IS NOT NULL AND \"IsDeleted\" = false");
        }
    }
}
