using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinMaster.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSafetyAndRestriction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRestricted",
                table: "WhatsAppAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDailyResetAt",
                table: "WhatsAppAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestrictedAt",
                table: "WhatsAppAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestrictionReason",
                table: "WhatsAppAccounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LimitResumeAt",
                table: "Campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PauseReason",
                table: "Campaigns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestrictionPausedAt",
                table: "Campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "CampaignCheckpoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRestricted",
                table: "WhatsAppAccounts");

            migrationBuilder.DropColumn(
                name: "LastDailyResetAt",
                table: "WhatsAppAccounts");

            migrationBuilder.DropColumn(
                name: "RestrictedAt",
                table: "WhatsAppAccounts");

            migrationBuilder.DropColumn(
                name: "RestrictionReason",
                table: "WhatsAppAccounts");

            migrationBuilder.DropColumn(
                name: "LimitResumeAt",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "PauseReason",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "RestrictionPausedAt",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "CampaignCheckpoints");
        }
    }
}
