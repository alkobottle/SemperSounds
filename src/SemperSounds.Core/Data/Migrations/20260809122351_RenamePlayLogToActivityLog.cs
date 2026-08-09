using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemperSounds.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePlayLogToActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    SoundId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SoundName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ChannelId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_OccurredAt",
                table: "ActivityLog",
                column: "OccurredAt");

            // Carry the existing history across before dropping the old table. EF scaffolded
            // a plain drop-and-create, which would have silently discarded every recorded
            // play. Kind 0 is Played, which is all the old table could hold.
            migrationBuilder.Sql(
                """
                INSERT INTO "ActivityLog"
                    ("Id", "Kind", "SoundId", "SoundName", "UserId", "UserName", "ChannelId", "ChannelName", "OccurredAt")
                SELECT "Id", 0, "SoundId", "SoundName", "UserId", "UserName", "ChannelId", NULL, "PlayedAt"
                FROM "PlayLog";
                """);

            migrationBuilder.DropTable(
                name: "PlayLog");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLog");

            migrationBuilder.CreateTable(
                name: "PlayLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SoundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SoundName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayLog_PlayedAt",
                table: "PlayLog",
                column: "PlayedAt");
        }
    }
}
