using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace The__Fix_WebApp.Data
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Products_IsActive_Category",
                table: "Products",
                columns: new[] { "IsActive", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DateCreated",
                table: "Orders",
                column: "DateCreated");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_OrderType",
                table: "Orders",
                columns: new[] { "Status", "OrderType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_IsActive_Category",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Name",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_DateCreated",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status_OrderType",
                table: "Orders");
        }
    }
}
