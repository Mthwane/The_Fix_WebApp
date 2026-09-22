using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace The__Fix_WebApp.Migrations
{
    /// <inheritdoc />
    public partial class FixSiteSettingsIdNotIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server can't ALTER COLUMN to remove IDENTITY in place - the column has to
            // be dropped and recreated. SiteSettings only ever holds one disposable row of
            // marketing copy, so drop/recreate is simpler and safer than a shadow-column dance.
            migrationBuilder.DropTable(name: "SiteSettings");

            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    HeroEyebrow = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    HeroHeadline = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    HeroSubheadline = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    HeroImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HeroPrimaryCtaText = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    HeroSecondaryCtaText = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StyleBoxHeadline = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StyleBoxSubheadline = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SiteSettings");

            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeroEyebrow = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    HeroHeadline = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    HeroSubheadline = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    HeroImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HeroPrimaryCtaText = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    HeroSecondaryCtaText = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StyleBoxHeadline = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StyleBoxSubheadline = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                });
        }
    }
}