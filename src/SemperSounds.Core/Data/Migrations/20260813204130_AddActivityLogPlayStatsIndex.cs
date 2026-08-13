using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemperSounds.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLogPlayStatsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_Kind_SoundId_OccurredAt",
                table: "ActivityLog",
                columns: new[] { "Kind", "SoundId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityLog_Kind_SoundId_OccurredAt",
                table: "ActivityLog");
        }
    }
}
