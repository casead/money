using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fix_PhysicalCash_IdZero_Orphan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data repair for the shop-creation bug: PhysicalCashAccount rows
            // were inserted with Id=0 because Shop.Id was read before its first
            // SaveChanges. No real Shop carries Id=0 (identity starts at 1),
            // so the orphan is removed and any shop still missing its own
            // cash-pool row is backfilled (idempotent).
            migrationBuilder.Sql(@"
                DELETE FROM ""PhysicalCashAccounts"" WHERE ""Id"" = 0;

                INSERT INTO ""PhysicalCashAccounts"" (""Id"", ""CurrentCashBalance"", ""UpdatedAtUtc"")
                SELECT s.""Id"", 0, NOW()
                FROM ""Shops"" s
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""PhysicalCashAccounts"" p WHERE p.""Id"" = s.""Id"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: data repair only.
        }
    }
}
