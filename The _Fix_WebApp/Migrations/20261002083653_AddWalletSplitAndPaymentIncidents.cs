using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace The__Fix_WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletSplitAndPaymentIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "WalletTransactions",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WalletAmountApplied",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
            migrationBuilder.Sql("UPDATE Orders SET WalletAmountApplied = GrandTotal WHERE PaymentMethod = 3");
            migrationBuilder.CreateTable(
                name: "PaymentIncidents",
                columns: table => new
                {
                    PaymentIncidentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    CardAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WalletAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PointsRedeemed = table.Column<int>(type: "int", nullable: false),
                    CardRefunded = table.Column<bool>(type: "bit", nullable: false),
                    WalletRestored = table.Column<bool>(type: "bit", nullable: false),
                    PointsRestored = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateResolved = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIncidents", x => x.PaymentIncidentId);
                    table.ForeignKey(
                        name: "FK_PaymentIncidents_AspNetUsers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_Reference",
                table: "WalletTransactions",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_Reference_Type",
                table: "WalletTransactions",
                columns: new[] { "Reference", "Type" },
                unique: true,
                filter: "[Reference] IS NOT NULL AND [Type] IN (0, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIncidents_CustomerId",
                table: "PaymentIncidents",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIncidents_Reference",
                table: "PaymentIncidents",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentIncidents");

            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_Reference",
                table: "WalletTransactions");

            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_Reference_Type",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "WalletAmountApplied",
                table: "Orders");
        }
    }
}
