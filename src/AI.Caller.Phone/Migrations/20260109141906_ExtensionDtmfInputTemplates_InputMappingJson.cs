using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class ExtensionDtmfInputTemplates_InputMappingJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InputMappingJson",
                table: "DtmfInputTemplates",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InputMappingJson",
                table: "DtmfInputTemplates");
        }
    }
}
