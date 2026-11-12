using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceTestsWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseFetching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentRunId",
                table: "PullRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PullRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContainerName = table.Column<string>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_Runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Runs_PullRequests_PullRequestId",
                        column: x => x.PullRequestId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                        name: "FK_Jobs_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_PullRequestId_ContainerName",
                table: "Runs",
                columns: new[] { "PullRequestId", "ContainerName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropColumn(
                name: "CurrentRunId",
                table: "PullRequests");
        }
    }
}
