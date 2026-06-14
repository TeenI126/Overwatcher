using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRankSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RankSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleRanks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RankSnapshotId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Division = table.Column<string>(type: "TEXT", nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    RankProgress = table.Column<int>(type: "INTEGER", nullable: true),
                    ChallengerScore = table.Column<int>(type: "INTEGER", nullable: true),
                    IsRanked = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlacementGames = table.Column<int>(type: "INTEGER", nullable: true),
                    PlacementRequired = table.Column<int>(type: "INTEGER", nullable: true),
                    RawText = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleRanks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleRanks_RankSnapshots_RankSnapshotId",
                        column: x => x.RankSnapshotId,
                        principalTable: "RankSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleRanks_RankSnapshotId",
                table: "RoleRanks",
                column: "RankSnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleRanks");

            migrationBuilder.DropTable(
                name: "RankSnapshots");
        }
    }
}
