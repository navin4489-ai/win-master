using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinMaster.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountIsDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "WhatsAppAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "WhatsAppAccounts");
        }
    }
}
