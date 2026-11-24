using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceTestsWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class DeriveCurrentAttemptFromStartTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PullRequests_Name",
                table: "PullRequests");

            migrationBuilder.DropIndex(
                name: "IX_PullRequests_PullRequest_Commit",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "PullRequests");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_PullRequest_Commit_StartTime",
                table: "PullRequests",
                columns: new[] { "PullRequest", "Commit", "StartTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PullRequests_PullRequest_Commit_StartTime",
                table: "PullRequests");

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "PullRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE \"PullRequests\" AS \"run\" SET \"IsCurrent\" = 1 " +
                "WHERE NOT EXISTS (SELECT 1 FROM \"PullRequests\" AS \"newer\" " +
                "WHERE \"newer\".\"PullRequest\" = \"run\".\"PullRequest\" " +
                "AND \"newer\".\"Commit\" = \"run\".\"Commit\" " +
                "AND (\"newer\".\"StartTime\" > \"run\".\"StartTime\" OR " +
                "(\"newer\".\"StartTime\" = \"run\".\"StartTime\" AND \"newer\".\"Id\" > \"run\".\"Id\")))");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_Name",
                table: "PullRequests",
                column: "Name",
                unique: true,
                filter: "\"IsCurrent\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_PullRequest_Commit",
                table: "PullRequests",
                columns: new[] { "PullRequest", "Commit" },
                unique: true,
                filter: "\"IsCurrent\" = 1");
        }
    }
}
