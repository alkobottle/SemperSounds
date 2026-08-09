using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemperSounds.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoundEmoji : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Emoji",
                table: "Sounds",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "🙂");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Emoji",
                table: "Sounds");
        }
    }
}
