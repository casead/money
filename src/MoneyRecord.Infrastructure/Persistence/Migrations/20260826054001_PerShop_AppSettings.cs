using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerShop_AppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_AppSettings_Key",
                table: "AppSettings");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AppSettings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            // Identity sequence on a pre-populated column must start past existing ids.
            migrationBuilder.Sql(
                @"SELECT setval(pg_get_serial_sequence('""AppSettings""','Id'),
                       (SELECT COALESCE(MAX(""Id""), 0) FROM ""AppSettings"") + 1, false);");

            migrationBuilder.AddColumn<long>(
                name: "ShopId",
                table: "AppSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "ShopId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 2,
                column: "ShopId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 3,
                column: "ShopId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 4,
                column: "ShopId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 5,
                column: "ShopId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 6,
                column: "ShopId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 7,
                column: "ShopId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 8,
                column: "ShopId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 9,
                column: "ShopId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 10,
                column: "ShopId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "UQ_AppSettings_Key_Shop",
                table: "AppSettings",
                columns: new[] { "Key", "ShopId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_AppSettings_Key_Shop",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ShopId",
                table: "AppSettings");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AppSettings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "UQ_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);
        }
    }
}
