using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class ExtendTTS : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Content",
                table: "TtsTemplates",
                type: "TEXT",
                nullable: false,
                comment: "包含占位符的内容模板, e.g., '您好{CustomerName}，欢迎使用我们的服务。'",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldComment: "支持模板语言（如Liquid）的字符串, e.g., '您好{% if gender == 'Male' %}先生{% else %}女士{% endif %}。'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Content",
                table: "TtsTemplates",
                type: "TEXT",
                nullable: false,
                comment: "支持模板语言（如Liquid）的字符串, e.g., '您好{% if gender == 'Male' %}先生{% else %}女士{% endif %}。'",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldComment: "包含占位符的内容模板, e.g., '您好{CustomerName}，欢迎使用我们的服务。'");
        }
    }
}
