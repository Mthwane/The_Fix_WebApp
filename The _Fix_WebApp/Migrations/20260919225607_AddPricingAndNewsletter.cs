using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace The__Fix_WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingAndNewsletter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CategoryPricingRules",
                columns: table => new
                {
                    CategoryPricingRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MarkupPercentage = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryPricingRules", x => x.CategoryPricingRuleId);
                });

            migrationBuilder.CreateTable(
                name: "EmailSubscribers",
                columns: table => new
                {
                    EmailSubscriberId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DateSubscribed = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSubscribers", x => x.EmailSubscriberId);
                });

            migrationBuilder.CreateTable(
                name: "PricingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DefaultMarkupPercentage = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShiftSessions",
                columns: table => new
                {
                    ShiftSessionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingFloat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DateOpened = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateClosed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftSessions", x => x.ShiftSessionId);
                    table.ForeignKey(
                        name: "FK_ShiftSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryPricingRules_Category",
                table: "CategoryPricingRules",
                column: "Category",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSubscribers_Email",
                table: "EmailSubscribers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSessions_UserId",
                table: "ShiftSessions",
                column: "UserId",
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryPricingRules");

            migrationBuilder.DropTable(
                name: "EmailSubscribers");

            migrationBuilder.DropTable(
                name: "PricingSettings");

            migrationBuilder.DropTable(
                name: "ShiftSessions");
        }
    }
}
