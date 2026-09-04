using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Syntera.Migrations.Platform
{
    /// <inheritdoc />
    public partial class AddUpnDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UpnDomain",
                table: "SiteLdapConfigs",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpnDomain",
                table: "SiteLdapConfigs");
        }
    }
}
