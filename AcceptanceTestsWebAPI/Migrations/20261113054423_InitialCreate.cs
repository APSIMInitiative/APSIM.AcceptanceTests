using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceTestsWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PullRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PullRequest = table.Column<string>(type: "TEXT", nullable: false),
                    Commit = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    NumberOfTasks = table.Column<int>(type: "INTEGER", nullable: false),
                    NumberOfTasksCompleted = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContainerName = table.Column<string>(type: "TEXT", nullable: true),
                    FetchStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FetchAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    DownloadedFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    FetchError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => new { x.RunId, x.Path });
                    table.ForeignKey(
                        name: "FK_Jobs_PullRequests_RunId",
                        column: x => x.RunId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_PullRequest_Commit_ContainerName",
                table: "PullRequests",
                columns: new[] { "PullRequest", "Commit", "ContainerName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "PullRequests");
        }
    }
}
