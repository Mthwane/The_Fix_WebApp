using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace The__Fix_WebApp.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_DeliveryReturnsHomepage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoutiqueHeadline",
                table: "SiteSettings",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoutiquePrimaryCtaText",
                table: "SiteSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoutiqueSecondaryCtaText",
                table: "SiteSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoutiqueText",
                table: "SiteSettings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentsEyebrow",
                table: "SiteSettings",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentsHeadline",
                table: "SiteSettings",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentsSubtext",
                table: "SiteSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeaturesText",
                table: "SiteSettings",
                type: "nvarchar(1200)",
                maxLength: 1200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeroStatsText",
                table: "SiteSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HideBoutique",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HideFeatures",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HidePromoStrip",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HidePromoStripWhenDiscountEnds",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HideStory",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HideStyleBox",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HideUtilityBar",
                table: "SiteSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PromoStripBgColor",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromoStripDiscountId",
                table: "SiteSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromoStripLinkText",
                table: "SiteSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromoStripLinkUrl",
                table: "SiteSettings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromoStripText",
                table: "SiteSettings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromoStripTextColor",
                table: "SiteSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryBody",
                table: "SiteSettings",
                type: "nvarchar(800)",
                maxLength: 800,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryChecklistText",
                table: "SiteSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryCtaText",
                table: "SiteSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryEyebrow",
                table: "SiteSettings",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryHeadline",
                table: "SiteSettings",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryImageUrl",
                table: "SiteSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StyleBoxEyebrow",
                table: "SiteSettings",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StyleBoxPrimaryCtaText",
                table: "SiteSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StyleBoxSecondaryCtaText",
                table: "SiteSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StyleBoxStepsText",
                table: "SiteSettings",
                type: "nvarchar(800)",
                maxLength: 800,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrendingEyebrow",
                table: "SiteSettings",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrendingHeadline",
                table: "SiteSettings",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtilityLeftText",
                table: "SiteSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtilityRightText",
                table: "SiteSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateCompleted",
                table: "ReturnTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "ReturnTransactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryFee",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoutiqueHeadline",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BoutiquePrimaryCtaText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BoutiqueSecondaryCtaText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BoutiqueText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "DepartmentsEyebrow",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "DepartmentsHeadline",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "DepartmentsSubtext",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "FeaturesText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HeroStatsText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HideBoutique",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HideFeatures",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HidePromoStrip",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HidePromoStripWhenDiscountEnds",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HideStory",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HideStyleBox",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "HideUtilityBar",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "PromoStripBgColor",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "PromoStripDiscountId",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "PromoStripLinkText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "PromoStripLinkUrl",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "PromoStripText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "PromoStripTextColor",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StoryBody",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StoryChecklistText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StoryCtaText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StoryEyebrow",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StoryHeadline",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StoryImageUrl",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StyleBoxEyebrow",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StyleBoxPrimaryCtaText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StyleBoxSecondaryCtaText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "StyleBoxStepsText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "TrendingEyebrow",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "TrendingHeadline",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "UtilityLeftText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "UtilityRightText",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "DateCompleted",
                table: "ReturnTransactions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ReturnTransactions");

            migrationBuilder.DropColumn(
                name: "DeliveryFee",
                table: "Orders");
        }
    }
}
