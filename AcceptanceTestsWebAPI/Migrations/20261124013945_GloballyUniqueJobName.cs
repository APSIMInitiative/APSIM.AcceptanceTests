using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceTestsWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class GloballyUniqueJobName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PullRequests_PullRequestNumber_Commit_Name",
                table: "PullRequests");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_Name",
                table: "PullRequests",
                column: "Name",
                unique: true,
                filter: "\"Name\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PullRequests_Name",
                table: "PullRequests");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_PullRequestNumber_Commit_Name",
                table: "PullRequests",
                columns: new[] { "PullRequestNumber", "Commit", "Name" },
                unique: true);
        }
    }
}
