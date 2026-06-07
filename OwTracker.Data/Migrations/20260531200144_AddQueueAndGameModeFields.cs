using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueAndGameModeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GameMode",
                table: "MatchRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QueueType",
                table: "MatchRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RankingMode",
                table: "MatchRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GameMode",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "QueueType",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "RankingMode",
                table: "MatchRecords");
        }
    }
}
