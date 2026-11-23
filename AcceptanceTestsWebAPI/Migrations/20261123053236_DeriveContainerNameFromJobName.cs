using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceTestsWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class DeriveContainerNameFromJobName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TEMP TABLE \"LegacyContainerValidation\" " +
                "(\"InvalidCount\" INTEGER CONSTRAINT \"ContainerNameMustEndInStorage\" CHECK (\"InvalidCount\" = 0)); " +
                "INSERT INTO \"LegacyContainerValidation\" SELECT count(*) FROM \"PullRequests\" " +
                "WHERE \"ContainerName\" IS NOT NULL AND " +
                "(length(\"ContainerName\") <= 8 OR substr(\"ContainerName\", -8) <> '-storage'); " +
                "DROP TABLE \"LegacyContainerValidation\";");

            migrationBuilder.Sql(
                "UPDATE \"PullRequests\" SET \"Name\" = CASE " +
                "WHEN \"ContainerName\" IS NULL THEN '' " +
                "ELSE substr(\"ContainerName\", 1, length(\"ContainerName\") - 8) END");

            migrationBuilder.DropIndex(
                name: "IX_PullRequests_PullRequest_Commit_ContainerName",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "ContainerName",
                table: "PullRequests");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PullRequests",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_PullRequest_Commit_Name",
                table: "PullRequests",
                columns: new[] { "PullRequest", "Commit", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PullRequests_PullRequest_Commit_Name",
                table: "PullRequests");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PullRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContainerName",
                table: "PullRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"PullRequests\" SET \"ContainerName\" = \"Name\" || '-storage' " +
                "WHERE \"Name\" IS NOT NULL AND \"Name\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_PullRequest_Commit_ContainerName",
                table: "PullRequests",
                columns: new[] { "PullRequest", "Commit", "ContainerName" },
                unique: true);
        }
    }
}
