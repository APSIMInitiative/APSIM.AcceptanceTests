using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceTestsWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class RenamePullRequestNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PullRequest",
                table: "PullRequests",
                newName: "PullRequestNumber");

            migrationBuilder.RenameIndex(
                name: "IX_PullRequests_PullRequest_Commit_StartTime",
                table: "PullRequests",
                newName: "IX_PullRequests_PullRequestNumber_Commit_StartTime");

            migrationBuilder.RenameIndex(
                name: "IX_PullRequests_PullRequest_Commit_Name",
                table: "PullRequests",
                newName: "IX_PullRequests_PullRequestNumber_Commit_Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PullRequestNumber",
                table: "PullRequests",
                newName: "PullRequest");

            migrationBuilder.RenameIndex(
                name: "IX_PullRequests_PullRequestNumber_Commit_StartTime",
                table: "PullRequests",
                newName: "IX_PullRequests_PullRequest_Commit_StartTime");

            migrationBuilder.RenameIndex(
                name: "IX_PullRequests_PullRequestNumber_Commit_Name",
                table: "PullRequests",
                newName: "IX_PullRequests_PullRequest_Commit_Name");
        }
    }
}
