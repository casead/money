using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Add_Txn_FeePaidVia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "FeePaidVia",
                table: "Transactions",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeePaidVia",
                table: "Transactions");
        }
    }
}
