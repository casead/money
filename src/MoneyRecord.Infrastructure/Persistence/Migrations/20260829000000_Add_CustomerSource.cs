using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Add Source column to Customers table for manual/auto customer tracking.
    /// </summary>
    public partial class Add_CustomerSource : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "auto");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "Customers");
        }
    }
}
