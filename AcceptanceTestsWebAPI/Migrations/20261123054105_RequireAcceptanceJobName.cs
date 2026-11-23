using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceTestsWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class RequireAcceptanceJobName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"PullRequests\" SET \"Name\" = '' WHERE \"Name\" IS NULL");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PullRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PullRequests",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }
    }
}
