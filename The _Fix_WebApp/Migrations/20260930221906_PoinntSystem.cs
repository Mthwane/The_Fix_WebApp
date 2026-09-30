using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace The__Fix_WebApp.Migrations
{
    /// <inheritdoc />
    public partial class PoinntSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RewardsAccounts",
                columns: table => new
                {
                    RewardsAccountId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Balance = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardsAccounts", x => x.RewardsAccountId);
                    table.ForeignKey(
                        name: "FK_RewardsAccounts_AspNetUsers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RewardsSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    EarnMode = table.Column<int>(type: "int", nullable: false),
                    FlatPointsPerPurchase = table.Column<int>(type: "int", nullable: false),
                    PointsPerRand = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    CashBackPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PointValueRands = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MinimumOrderAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinimumPointsToRedeem = table.Column<int>(type: "int", nullable: false),
                    MaxRedeemPercentOfOrder = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardsSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RewardsTransactions",
                columns: table => new
                {
                    RewardsTransactionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RewardsAccountId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    BalanceAfter = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardsTransactions", x => x.RewardsTransactionId);
                    table.ForeignKey(
                        name: "FK_RewardsTransactions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RewardsTransactions_RewardsAccounts_RewardsAccountId",
                        column: x => x.RewardsAccountId,
                        principalTable: "RewardsAccounts",
                        principalColumn: "RewardsAccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RewardsAccounts_CustomerId",
                table: "RewardsAccounts",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RewardsTransactions_OrderId_Type",
                table: "RewardsTransactions",
                columns: new[] { "OrderId", "Type" },
                unique: true,
                filter: "[OrderId] IS NOT NULL AND [Type] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RewardsTransactions_Reference",
                table: "RewardsTransactions",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_RewardsTransactions_RewardsAccountId",
                table: "RewardsTransactions",
                column: "RewardsAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RewardsSettings");

            migrationBuilder.DropTable(
                name: "RewardsTransactions");

            migrationBuilder.DropTable(
                name: "RewardsAccounts");
        }
    }
}
