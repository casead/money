using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PG hardening: default tenant shop (id=1), ledger append-only guards
    /// (DR-02/03 — UPDATE/DELETE denied on CashLedgerEntries/WalletLedgerEntries).
    /// </summary>
    public partial class LedgerAppendOnlyGuards : Migration
    {
        private static void CreateGuards(MigrationBuilder b, string table)
        {
            // One shared guard function per action, reused by both ledger tables.
            b.Sql($@"
CREATE OR REPLACE FUNCTION trg_{table}_block_update() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'Ledger {table} is append-only (DR-02/03): UPDATE denied.';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER TR_{table}_BlockUpdate
BEFORE UPDATE ON ""{table}""
FOR EACH ROW EXECUTE FUNCTION trg_{table}_block_update();");

            b.Sql($@"
CREATE OR REPLACE FUNCTION trg_{table}_block_delete() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'Ledger {table} is append-only (DR-02/03): DELETE denied.';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER TR_{table}_BlockDelete
BEFORE DELETE ON ""{table}""
FOR EACH ROW EXECUTE FUNCTION trg_{table}_block_delete();");
        }

        private static void DropGuards(MigrationBuilder b, string table)
        {
            b.Sql($"DROP TRIGGER IF EXISTS TR_{table}_BlockUpdate ON \"{table}\";");
            b.Sql($"DROP FUNCTION IF EXISTS trg_{table}_block_update();");
            b.Sql($"DROP TRIGGER IF EXISTS TR_{table}_BlockDelete ON \"{table}\";");
            b.Sql($"DROP FUNCTION IF EXISTS trg_{table}_block_delete();");
        }

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default shop (M11): Id=1 = MoneyRecord Agent Shop; identity re-aligned past it.
            migrationBuilder.Sql(@"INSERT INTO ""Shops"" (""Id"", ""Code"", ""Name"", ""Status"", ""CreatedAtUtc"")
SELECT 1, 'DEFAULT', 'MoneyRecord Agent Shop', 1, '2026-01-01T00:00:00Z'::timestamptz
WHERE NOT EXISTS (SELECT 1 FROM ""Shops"" WHERE ""Id"" = 1);
SELECT setval('""Shops_Id_seq""', GREATEST((SELECT COALESCE(MAX(""Id""), 0) FROM ""Shops""), 1), true);");

            CreateGuards(migrationBuilder, "CashLedgerEntries");
            CreateGuards(migrationBuilder, "WalletLedgerEntries");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropGuards(migrationBuilder, "CashLedgerEntries");
            DropGuards(migrationBuilder, "WalletLedgerEntries");
        }
    }
}
