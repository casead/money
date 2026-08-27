using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Backfill_PhysicalCash_PerShop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M11 backfill — every shop missing its cash-pool row gets one (0 balance).
            migrationBuilder.Sql(@"
                INSERT INTO ""PhysicalCashAccounts"" (""Id"", ""CurrentCashBalance"", ""UpdatedAtUtc"")
                SELECT s.""Id"", 0, NOW()
                FROM ""Shops"" s
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""PhysicalCashAccounts"" p WHERE p.""Id"" = s.""Id"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: never delete existing cash pools.
        }
    }
}
