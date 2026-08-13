using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemperSounds.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEntrySounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntrySoundBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BlockedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    BlockedByName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BlockedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntrySoundBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EntrySounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    SoundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsMuted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AssignedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntrySounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntrySounds_Sounds_SoundId",
                        column: x => x.SoundId,
                        principalTable: "Sounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EntrySoundSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SnoozedUntil = table.Column<long>(type: "INTEGER", nullable: true),
                    VolumePercent = table.Column<int>(type: "INTEGER", nullable: false),
                    PerUserCooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedByUserId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntrySoundSettings", x => x.Id);
                    table.CheckConstraint("CK_EntrySoundSettings_Singleton", "Id = 1");
                });

            migrationBuilder.InsertData(
                table: "EntrySoundSettings",
                columns: new[] { "Id", "IsEnabled", "MaxDurationMs", "PerUserCooldownSeconds", "SnoozedUntil", "UpdatedAt", "UpdatedByUserId", "VolumePercent" },
                values: new object[] { 1, true, 5000, 60, null, 639028224000000000L, null, 70 });

            migrationBuilder.CreateIndex(
                name: "IX_EntrySoundBlocks_UserId",
                table: "EntrySoundBlocks",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntrySounds_SoundId",
                table: "EntrySounds",
                column: "SoundId");

            migrationBuilder.CreateIndex(
                name: "IX_EntrySounds_UserId",
                table: "EntrySounds",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntrySoundBlocks");

            migrationBuilder.DropTable(
                name: "EntrySounds");

            migrationBuilder.DropTable(
                name: "EntrySoundSettings");
        }
    }
}
