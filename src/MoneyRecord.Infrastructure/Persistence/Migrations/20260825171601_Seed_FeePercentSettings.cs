using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Seed_FeePercentSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Id", "Key", "UpdatedAtUtc", "UpdatedByUserId", "Value", "ValueType" },
                values: new object[,]
                {
                    { 9, "feePercentCashIn", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0", "percent" },
                    { 10, "feePercentCashOut", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0", "percent" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 10);
        }
    }
}
