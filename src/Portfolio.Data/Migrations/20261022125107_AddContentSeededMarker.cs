using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentSeededMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deliberately not backfilled: a site that has never completed a start-up seeding pass must stay unpinned, so its first start after
            // the upgrade seeds exactly as it always did and only then records the marker.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContentSeededAt",
                table: "SiteProfiles",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentSeededAt",
                table: "SiteProfiles");
        }
    }
}
