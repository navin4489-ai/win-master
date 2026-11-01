using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinMaster.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignConnectionPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionMessage",
                table: "Campaigns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PausedByConnection",
                table: "Campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionMessage",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "PausedByConnection",
                table: "Campaigns");
        }
    }
}
