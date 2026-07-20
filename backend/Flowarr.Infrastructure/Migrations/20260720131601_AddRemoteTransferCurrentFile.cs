using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowarr.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteTransferCurrentFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentFile",
                table: "RemoteTransfers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentFile",
                table: "RemoteTransfers");
        }
    }
}
