using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemperSounds.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SoundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SoundName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ChannelId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    UploaderId = table.Column<long>(type: "INTEGER", nullable: false),
                    UploaderName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sounds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayLog_PlayedAt",
                table: "PlayLog",
                column: "PlayedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sounds_Name",
                table: "Sounds",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayLog");

            migrationBuilder.DropTable(
                name: "Sounds");
        }
    }
}
