using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    MatchDatetime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GameLength = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    MyTeamScore = table.Column<int>(type: "INTEGER", nullable: false),
                    EnemyTeamScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    ScrapedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingHeroLabels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CropPath = table.Column<string>(type: "TEXT", nullable: false),
                    PredictedHero = table.Column<string>(type: "TEXT", nullable: false),
                    Confidence = table.Column<float>(type: "REAL", nullable: false),
                    ConfirmedHero = table.Column<string>(type: "TEXT", nullable: true),
                    Reviewed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingHeroLabels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SessionEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActiveDuration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    TotalOpenDuration = table.Column<TimeSpan>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsMe = table.Column<bool>(type: "INTEGER", nullable: false),
                    Team = table.Column<string>(type: "TEXT", nullable: false),
                    EndingHero = table.Column<string>(type: "TEXT", nullable: false),
                    Eliminations = table.Column<int>(type: "INTEGER", nullable: false),
                    Assists = table.Column<int>(type: "INTEGER", nullable: false),
                    Deaths = table.Column<int>(type: "INTEGER", nullable: false),
                    DamageDealt = table.Column<int>(type: "INTEGER", nullable: false),
                    HealingDone = table.Column<int>(type: "INTEGER", nullable: false),
                    DamageMitigated = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerRecords_MatchRecords_MatchRecordId",
                        column: x => x.MatchRecordId,
                        principalTable: "MatchRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HeroPlaytimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    HeroName = table.Column<string>(type: "TEXT", nullable: false),
                    TimePlayed = table.Column<TimeSpan>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeroPlaytimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HeroPlaytimes_PlayerRecords_PlayerRecordId",
                        column: x => x.PlayerRecordId,
                        principalTable: "PlayerRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HeroPlaytimes_PlayerRecordId",
                table: "HeroPlaytimes",
                column: "PlayerRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchRecords_MapName_MatchDatetime",
                table: "MatchRecords",
                columns: new[] { "MapName", "MatchDatetime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRecords_MatchRecordId",
                table: "PlayerRecords",
                column: "MatchRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeroPlaytimes");

            migrationBuilder.DropTable(
                name: "PendingHeroLabels");

            migrationBuilder.DropTable(
                name: "SessionRecords");

            migrationBuilder.DropTable(
                name: "PlayerRecords");

            migrationBuilder.DropTable(
                name: "MatchRecords");
        }
    }
}
