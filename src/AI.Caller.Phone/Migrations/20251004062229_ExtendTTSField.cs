using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class ExtendTTSField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EndingSpeech",
                table: "TtsTemplates",
                type: "TEXT",
                nullable: true,
                comment: "循环播放结束后，最终播报一次的内容。");

            migrationBuilder.AddColumn<int>(
                name: "PauseBetweenPlaysInSeconds",
                table: "TtsTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndingSpeech",
                table: "TtsTemplates");

            migrationBuilder.DropColumn(
                name: "PauseBetweenPlaysInSeconds",
                table: "TtsTemplates");
        }
    }
}
