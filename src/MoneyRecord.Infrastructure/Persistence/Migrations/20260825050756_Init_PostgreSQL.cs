using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MoneyRecord.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Init_PostgreSQL : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "TxnNoSeq");

            migrationBuilder.CreateTable(
                name: "AdjustmentTypes",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjustmentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ValueType = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    IsSensitive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    UpdatedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActionCode = table.Column<string>(type: "character varying(40)", unicode: false, maxLength: 40, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: false),
                    OldValuesJson = table.Column<string>(type: "text", nullable: true),
                    NewValuesJson = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", unicode: false, maxLength: 45, nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    ShopId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommissionSources",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyKeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: false),
                    Module = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhysicalCashAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    CurrentCashBalance = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    LastReconciledAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalCashAccounts", x => x.Id);
                    table.CheckConstraint("CK_PhysicalCash_Nonneg", "\"CurrentCashBalance\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsSystemRole = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionStatuses",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "character varying(15)", unicode: false, maxLength: 15, nullable: false),
                    Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionTypes",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    PermissionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", unicode: false, maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp(0) with time zone", nullable: true),
                    FailedLoginCount = table.Column<short>(type: "smallint", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Username_Length", "LENGTH(\"Username\") >= 3");
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeeRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletProviderId = table.Column<int>(type: "integer", nullable: false),
                    CalculationType = table.Column<byte>(type: "smallint", nullable: false),
                    FlatAmount = table.Column<long>(type: "bigint", nullable: true),
                    PercentValue = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    MinFee = table.Column<long>(type: "bigint", nullable: true),
                    MaxFee = table.Column<long>(type: "bigint", nullable: true),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeeRules", x => x.Id);
                    table.CheckConstraint("CK_FeeRule_Flat_Positive", "\"CalculationType\" <> 1 OR \"FlatAmount\" > 0");
                    table.CheckConstraint("CK_FeeRule_MinMax", "\"MinFee\" IS NULL OR \"MaxFee\" IS NULL OR \"MaxFee\" >= \"MinFee\"");
                    table.CheckConstraint("CK_FeeRule_Percent_Range", "\"CalculationType\" <> 2 OR (\"PercentValue\" > 0 AND \"PercentValue\" <= 100)");
                    table.ForeignKey(
                        name: "FK_FeeRules_WalletProviders_WalletProviderId",
                        column: x => x.WalletProviderId,
                        principalTable: "WalletProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalletAccounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShopId = table.Column<long>(type: "bigint", nullable: false),
                    WalletProviderId = table.Column<int>(type: "integer", nullable: false),
                    AccountName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: true),
                    CurrentFloatBalance = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletAccounts", x => x.Id);
                    table.CheckConstraint("CK_WalletAccounts_Float_Nonneg", "\"CurrentFloatBalance\" >= 0");
                    table.ForeignKey(
                        name: "FK_WalletAccounts_WalletProviders_WalletProviderId",
                        column: x => x.WalletProviderId,
                        principalTable: "WalletProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashAdjustments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdjustmentTypeId = table.Column<byte>(type: "smallint", nullable: false),
                    Direction = table.Column<byte>(type: "smallint", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashAdjustments", x => x.Id);
                    table.CheckConstraint("CK_CashAdj_Amount_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CashAdjustments_AdjustmentTypes_AdjustmentTypeId",
                        column: x => x.AdjustmentTypeId,
                        principalTable: "AdjustmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashAdjustments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Direction = table.Column<byte>(type: "smallint", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<byte>(type: "smallint", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CashAdjustmentId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashLedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_CashLedger_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CashLedger_Source_Xor", "(\"SourceType\" = 1 AND \"TransactionId\" IS NOT NULL AND \"CashAdjustmentId\" IS NULL) OR (\"SourceType\" = 2 AND \"CashAdjustmentId\" IS NOT NULL AND \"TransactionId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_CashLedgerEntries_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RegisteredByShopId = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.CheckConstraint("CK_Customers_FullName_Length", "LENGTH(\"FullName\") >= 2");
                    table.ForeignKey(
                        name: "FK_Customers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_Users_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", unicode: false, maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FloatAdjustments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletAccountId = table.Column<long>(type: "bigint", nullable: false),
                    AdjustmentTypeId = table.Column<byte>(type: "smallint", nullable: false),
                    Direction = table.Column<byte>(type: "smallint", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FloatAdjustments", x => x.Id);
                    table.CheckConstraint("CK_FloatAdj_Amount_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_FloatAdjustments_AdjustmentTypes_AdjustmentTypeId",
                        column: x => x.AdjustmentTypeId,
                        principalTable: "AdjustmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FloatAdjustments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FloatAdjustments_WalletAccounts_WalletAccountId",
                        column: x => x.WalletAccountId,
                        principalTable: "WalletAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TxnNo = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Type = table.Column<byte>(type: "smallint", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    FeeAmount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    FeeRuleId = table.Column<int>(type: "integer", nullable: true),
                    FeeOverridden = table.Column<bool>(type: "boolean", nullable: false),
                    CommissionAmount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomerPhoneSnapshot = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    WalletProviderId = table.Column<int>(type: "integer", nullable: false),
                    WalletAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReferenceNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversedByTxnId = table.Column<long>(type: "bigint", nullable: true),
                    ReversalOfTxnId = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    CancelledByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ReversedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ReversalReason = table.Column<string>(type: "text", nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Txn_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_Txn_CommNonNeg", "\"CommissionAmount\" >= 0");
                    table.CheckConstraint("CK_Txn_FeeNonNeg", "\"FeeAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_Transactions_WalletAccounts_WalletAccountId",
                        column: x => x.WalletAccountId,
                        principalTable: "WalletAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_WalletProviders_WalletProviderId",
                        column: x => x.WalletProviderId,
                        principalTable: "WalletProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalletLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletAccountId = table.Column<long>(type: "bigint", nullable: false),
                    Direction = table.Column<byte>(type: "smallint", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<byte>(type: "smallint", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true),
                    FloatAdjustmentId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletLedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_WalletLedger_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_WalletLedger_Source_Xor", "(\"SourceType\" = 1 AND \"TransactionId\" IS NOT NULL AND \"FloatAdjustmentId\" IS NULL) OR (\"SourceType\" = 2 AND \"FloatAdjustmentId\" IS NOT NULL AND \"TransactionId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_WalletLedgerEntries_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletLedgerEntries_WalletAccounts_WalletAccountId",
                        column: x => x.WalletAccountId,
                        principalTable: "WalletAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommissionEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true),
                    BatchRef = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: true),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    SourceId = table.Column<byte>(type: "smallint", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionEntries", x => x.Id);
                    table.CheckConstraint("CK_CommissionEntry_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CommissionEntry_Source_Xor", "(\"TransactionId\" IS NOT NULL AND \"BatchRef\" IS NULL) OR (\"BatchRef\" IS NOT NULL AND \"TransactionId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_CommissionEntries_CommissionSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "CommissionSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionEntries_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionCancellations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CancelledByUserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionCancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionCancellations_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionReversals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OriginalTxnId = table.Column<long>(type: "bigint", nullable: false),
                    MirrorTxnId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    ReversedByUserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionReversals", x => x.Id);
                    table.CheckConstraint("CK_TransactionReversals_NotSelf", "\"OriginalTxnId\" <> \"MirrorTxnId\"");
                    table.ForeignKey(
                        name: "FK_TransactionReversals_Transactions_MirrorTxnId",
                        column: x => x.MirrorTxnId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionReversals_Transactions_OriginalTxnId",
                        column: x => x.OriginalTxnId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AdjustmentTypes",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { (byte)1, "CashCorrection", "Cash Correction" },
                    { (byte)2, "FloatTopUp", "Float Top-up" },
                    { (byte)3, "FloatWithdrawal", "Float Withdrawal" }
                });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Id", "Key", "UpdatedAtUtc", "UpdatedByUserId", "Value", "ValueType" },
                values: new object[] { 1, "shopName", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "????????? ?????", "string" });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Id", "IsSensitive", "Key", "UpdatedAtUtc", "UpdatedByUserId", "Value", "ValueType" },
                values: new object[] { 2, true, "dayBoundaryOffsetHours", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0", "int" });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Id", "Key", "UpdatedAtUtc", "UpdatedByUserId", "Value", "ValueType" },
                values: new object[,]
                {
                    { 3, "pendingExpiryMinutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30", "int" },
                    { 4, "duplicateWindowMinutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "5", "int" },
                    { 5, "txnAmountCap", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "10000000", "int" },
                    { 6, "lowBalanceCashThreshold", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "100000", "int" },
                    { 7, "lowBalanceFloatThresholdPerAccount", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "50000", "int" },
                    { 8, "receiptFooterText", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "???????????????", "string" }
                });

            migrationBuilder.InsertData(
                table: "CommissionSources",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { (byte)1, "PerTxnAuto", "Per-Txn Auto" },
                    { (byte)2, "PerTxnManual", "Per-Txn Manual" },
                    { (byte)3, "PeriodicBatch", "Periodic Batch" },
                    { (byte)4, "Adjustment", "Adjustment" }
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "Module" },
                values: new object[,]
                {
                    { 1, "txn.create", "txn.create permission", "Txn" },
                    { 2, "txn.cancel", "txn.cancel permission", "Txn" },
                    { 3, "txn.reverse", "txn.reverse permission", "Txn" },
                    { 4, "customer.manage", "customer.manage permission", "Customer" },
                    { 5, "balance.adjust", "balance.adjust permission", "Balance" },
                    { 6, "fee.manage", "fee.manage permission", "Fee" },
                    { 7, "tenant.manage", "tenant.manage permission", "Platform" },
                    { 8, "provider.manage", "provider.manage permission", "Admin" },
                    { 9, "user.manage", "user.manage permission", "Admin" },
                    { 10, "audit.view", "audit.view permission", "Admin" },
                    { 11, "settings.manage", "settings.manage permission", "Admin" },
                    { 12, "report.daily", "report.daily permission", "Report" },
                    { 13, "report.profit", "report.profit permission", "Report" }
                });

            migrationBuilder.InsertData(
                table: "PhysicalCashAccounts",
                columns: new[] { "Id", "LastReconciledAtUtc", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { 1, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Code", "Description", "IsSystemRole", "Name" },
                values: new object[,]
                {
                    { 1, "SuperAdmin", "Platform owner", true, "Super Admin" },
                    { 2, "Admin", "Shop administrator", true, "Admin" },
                    { 3, "Staff", "Shop staff", true, "Staff" }
                });

            migrationBuilder.InsertData(
                table: "TransactionStatuses",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { (byte)1, "Pending", "Pending" },
                    { (byte)2, "Completed", "Completed" },
                    { (byte)3, "Cancelled", "Cancelled" },
                    { (byte)4, "Reversed", "Reversed" }
                });

            migrationBuilder.InsertData(
                table: "TransactionTypes",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { (byte)1, "CashIn", "Cash In" },
                    { (byte)2, "CashOut", "Cash Out" }
                });

            migrationBuilder.InsertData(
                table: "WalletProviders",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "LogoUrl", "Name" },
                values: new object[,]
                {
                    { 1, "WAVE", 1, true, null, "Wave Money" },
                    { 2, "KBZPAY", 2, true, null, "KBZPay" }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { 6, 1 },
                    { 7, 1 },
                    { 8, 1 },
                    { 9, 1 },
                    { 10, 1 },
                    { 11, 1 },
                    { 1, 2 },
                    { 2, 2 },
                    { 3, 2 },
                    { 4, 2 },
                    { 5, 2 },
                    { 9, 2 },
                    { 10, 2 },
                    { 11, 2 },
                    { 12, 2 },
                    { 13, 2 },
                    { 1, 3 },
                    { 4, 3 },
                    { 12, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "UQ_AdjustmentTypes_Code",
                table: "AdjustmentTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActionCode_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "ActionCode", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Actor_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "ActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ShopId_CreatedAt",
                table: "AuditLogs",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_CashAdjustments_AdjustmentTypeId",
                table: "CashAdjustments",
                column: "AdjustmentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CashAdjustments_CreatedByUserId",
                table: "CashAdjustments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashLedger_CashAdjustmentId",
                table: "CashLedgerEntries",
                column: "CashAdjustmentId",
                filter: "\"CashAdjustmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashLedger_CreatedAt",
                table: "CashLedgerEntries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CashLedger_TransactionId",
                table: "CashLedgerEntries",
                column: "TransactionId",
                filter: "\"TransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashLedgerEntries_CreatedByUserId",
                table: "CashLedgerEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionEntries_CreatedAt",
                table: "CommissionEntries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionEntries_SourceId",
                table: "CommissionEntries",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionEntries_TransactionId",
                table: "CommissionEntries",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "UQ_CommissionSources_Code",
                table: "CommissionSources",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedByUserId",
                table: "Customers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_FullName",
                table: "Customers",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ModifiedByUserId",
                table: "Customers",
                column: "ModifiedByUserId");

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

            migrationBuilder.CreateIndex(
                name: "IX_FeeRules_Provider_EffectiveFrom",
                table: "FeeRules",
                columns: new[] { "WalletProviderId", "EffectiveFromUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_FloatAdjustments_Account",
                table: "FloatAdjustments",
                column: "WalletAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FloatAdjustments_AdjustmentTypeId",
                table: "FloatAdjustments",
                column: "AdjustmentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_FloatAdjustments_CreatedByUserId",
                table: "FloatAdjustments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_ExpiresAt",
                table: "IdempotencyKeys",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "UQ_IdempotencyKeys_Key",
                table: "IdempotencyKeys",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Permissions_Code",
                table: "Permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_User_Revoked",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UQ_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "UQ_Roles_Code",
                table: "Roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Shops_Code",
                table: "Shops",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_TransactionCancellations_Txn",
                table: "TransactionCancellations",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_TransactionReversals_Mirror",
                table: "TransactionReversals",
                column: "MirrorTxnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_TransactionReversals_Original",
                table: "TransactionReversals",
                column: "OriginalTxnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BusinessDate_Type_Status",
                table: "Transactions",
                columns: new[] { "BusinessDate", "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CreatedBy_BusinessDate",
                table: "Transactions",
                columns: new[] { "CreatedByUserId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CustomerId_BusinessDate",
                table: "Transactions",
                columns: new[] { "CustomerId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CustomerPhoneSnapshot",
                table: "Transactions",
                column: "CustomerPhoneSnapshot");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ReferenceNo",
                table: "Transactions",
                column: "ReferenceNo");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ShopId_BusinessDate",
                table: "Transactions",
                columns: new[] { "ShopId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_WalletAccountId",
                table: "Transactions",
                column: "WalletAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_WalletProviderId",
                table: "Transactions",
                column: "WalletProviderId");

            migrationBuilder.CreateIndex(
                name: "UQ_Transactions_IdempotencyKey",
                table: "Transactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Transactions_TxnNo",
                table: "Transactions",
                column: "TxnNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionStatuses_Code",
                table: "TransactionStatuses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionTypes_Code",
                table: "TransactionTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Phone",
                table: "Users",
                column: "Phone",
                unique: true,
                filter: "\"Phone\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ShopId",
                table: "Users",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "UQ_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletAccounts_ProviderId",
                table: "WalletAccounts",
                column: "WalletProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAccounts_ShopId",
                table: "WalletAccounts",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "UQ_WalletAccounts_AccountNumber",
                table: "WalletAccounts",
                column: "AccountNumber",
                unique: true,
                filter: "\"AccountNumber\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_WalletLedger_AccountId_CreatedAt",
                table: "WalletLedgerEntries",
                columns: new[] { "WalletAccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletLedgerEntries_CreatedByUserId",
                table: "WalletLedgerEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_WalletProviders_Code",
                table: "WalletProviders",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CashAdjustments");

            migrationBuilder.DropTable(
                name: "CashLedgerEntries");

            migrationBuilder.DropTable(
                name: "CommissionEntries");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "FeeRules");

            migrationBuilder.DropTable(
                name: "FloatAdjustments");

            migrationBuilder.DropTable(
                name: "IdempotencyKeys");

            migrationBuilder.DropTable(
                name: "PhysicalCashAccounts");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "TransactionCancellations");

            migrationBuilder.DropTable(
                name: "TransactionReversals");

            migrationBuilder.DropTable(
                name: "TransactionStatuses");

            migrationBuilder.DropTable(
                name: "TransactionTypes");

            migrationBuilder.DropTable(
                name: "WalletLedgerEntries");

            migrationBuilder.DropTable(
                name: "CommissionSources");

            migrationBuilder.DropTable(
                name: "AdjustmentTypes");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WalletAccounts");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Shops");

            migrationBuilder.DropTable(
                name: "WalletProviders");

            migrationBuilder.DropSequence(
                name: "TxnNoSeq");
        }
    }
}
