using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSrChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SrChange",
                table: "MatchRecords",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SrChange",
                table: "MatchRecords");
        }
    }
}
