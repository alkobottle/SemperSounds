using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemperSounds.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Favorites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    SoundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Favorites_Sounds_SoundId",
                        column: x => x.SoundId,
                        principalTable: "Sounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_SoundId",
                table: "Favorites",
                column: "SoundId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId_Slot",
                table: "Favorites",
                columns: new[] { "UserId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId_SoundId",
                table: "Favorites",
                columns: new[] { "UserId", "SoundId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Favorites");
        }
    }
}
