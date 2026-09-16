using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceTestsWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class v2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Number",
                table: "PullRequests",
                newName: "PullRequest");

            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "PullRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Author",
                table: "PullRequests");

            migrationBuilder.RenameColumn(
                name: "PullRequest",
                table: "PullRequests",
                newName: "Number");
        }
    }
}
