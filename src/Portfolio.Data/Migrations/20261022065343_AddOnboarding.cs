using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "SiteProfiles",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingCompletedAt",
                table: "SiteProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnboardingDraft",
                table: "SiteProfiles",
                type: "nvarchar(max)",
                nullable: true);

            // A profile row that exists now belongs to a site that was set up before the wizard existed: it counts as
            // finished, so an upgrade never sends its owner into onboarding. A database without a row (a new install) gets one
            // later, with no value, and is offered the wizard.
            migrationBuilder.Sql("UPDATE [SiteProfiles] SET [OnboardingCompletedAt] = '2026-10-22T00:00:00+00:00' WHERE [OnboardingCompletedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Location",
                table: "SiteProfiles");

            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAt",
                table: "SiteProfiles");

            migrationBuilder.DropColumn(
                name: "OnboardingDraft",
                table: "SiteProfiles");
        }
    }
}
