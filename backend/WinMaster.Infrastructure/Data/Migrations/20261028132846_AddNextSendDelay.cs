using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinMaster.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNextSendDelay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextSendDelaySec",
                table: "CampaignCheckpoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextSendDelaySec",
                table: "CampaignCheckpoints");
        }
    }
}
