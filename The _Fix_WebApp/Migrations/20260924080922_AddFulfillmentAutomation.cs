using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace The__Fix_WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddFulfillmentAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDate", table: "PurchaseOrders", type: "datetime2", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod", table: "PurchaseOrders", type: "int", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference", table: "PurchaseOrders", type: "nvarchar(100)", maxLength: 100, nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentStatus", table: "PurchaseOrders", type: "int", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustomerConfirmedDeliveryAt", table: "Orders", type: "datetime2", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            

            migrationBuilder.DropColumn(
                name: "PaymentDate",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CustomerConfirmedDeliveryAt",
                table: "Orders");
        }
    }
}
