using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinMaster.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQrPngBase64 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QrPngBase64",
                table: "WhatsAppAccounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QrPngBase64",
                table: "WhatsAppAccounts");
        }
    }
}
