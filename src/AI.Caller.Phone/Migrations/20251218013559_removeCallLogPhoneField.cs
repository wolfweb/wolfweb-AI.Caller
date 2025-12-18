using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class removeCallLogPhoneField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "CallLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "CallLogs",
                type: "TEXT",
                nullable: true);
        }
    }
}
