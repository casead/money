using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Customers_ShopTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Staging column (nullable) — backfilled before the NOT NULL flip.
            migrationBuilder.AddColumn<long>(
                name: "ShopId",
                table: "Customers",
                type: "bigint",
                nullable: true);

            // 2) Backfill: provenance shop wins; legacy rows without provenance
            //    are adopted by the first shop so no row is orphaned.
            migrationBuilder.Sql(@"
                UPDATE ""Customers""
                SET ""ShopId"" = COALESCE(""RegisteredByShopId"", (SELECT MIN(""Id"") FROM ""Shops""))
                WHERE ""ShopId"" IS NULL;");

            // 3) Enforce NOT NULL.
            migrationBuilder.AlterColumn<long>(
                name: "ShopId",
                table: "Customers",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_Customers_RegisteredByShopId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "UQ_Customers_Phone",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RegisteredByShopId",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ShopId",
                table: "Customers",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "UQ_Customers_Shop_Phone",
                table: "Customers",
                columns: new[] { "ShopId", "Phone" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Shops_ShopId",
                table: "Customers",
                column: "ShopId",
                principalTable: "Shops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Shops_ShopId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_ShopId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "UQ_Customers_Shop_Phone",
                table: "Customers");

            migrationBuilder.AddColumn<long>(
                name: "RegisteredByShopId",
                table: "Customers",
                type: "bigint",
                nullable: true);

            // Restore provenance from the owning shop before dropping it.
            migrationBuilder.Sql(@"
                UPDATE ""Customers""
                SET ""RegisteredByShopId"" = ""ShopId""
                WHERE ""RegisteredByShopId"" IS NULL;");

            migrationBuilder.DropColumn(
                name: "ShopId",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_RegisteredByShopId",
                table: "Customers",
                column: "RegisteredByShopId");

            migrationBuilder.CreateIndex(
                name: "UQ_Customers_Phone",
                table: "Customers",
                column: "Phone",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
