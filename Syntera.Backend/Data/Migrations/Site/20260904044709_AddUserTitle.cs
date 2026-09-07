using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Syntera.Backend.Data.Migrations.Site
{
    /// <inheritdoc />
    public partial class AddUserTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Users",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "Users");
        }
    }
}
