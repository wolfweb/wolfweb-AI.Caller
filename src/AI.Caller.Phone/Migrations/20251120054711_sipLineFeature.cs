using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class sipLineFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultLineId",
                table: "SipAccounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoSelectLine",
                table: "BatchCallJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SelectedLineId",
                table: "BatchCallJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SipLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProxyServer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OutboundProxy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Region = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SipLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SipAccountSipLine",
                columns: table => new
                {
                    SipAccountsId = table.Column<int>(type: "INTEGER", nullable: false),
                    AvailableLinesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SipAccountSipLine", x => new { x.SipAccountsId, x.AvailableLinesId });
                    table.ForeignKey(
                        name: "FK_SipAccountSipLine_SipAccounts_SipAccountsId",
                        column: x => x.SipAccountsId,
                        principalTable: "SipAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SipAccountSipLine_SipLines_AvailableLinesId",
                        column: x => x.AvailableLinesId,
                        principalTable: "SipLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SipAccounts_DefaultLineId",
                table: "SipAccounts",
                column: "DefaultLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SipAccountSipLine_AvailableLinesId",
                table: "SipAccountSipLine",
                column: "AvailableLinesId");

            migrationBuilder.CreateIndex(
                name: "IX_SipLines_IsActive",
                table: "SipLines",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SipLines_Priority",
                table: "SipLines",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_SipLines_Region",
                table: "SipLines",
                column: "Region");

            migrationBuilder.AddForeignKey(
                name: "FK_SipAccounts_SipLines_DefaultLineId",
                table: "SipAccounts",
                column: "DefaultLineId",
                principalTable: "SipLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SipAccounts_SipLines_DefaultLineId",
                table: "SipAccounts");

            migrationBuilder.DropTable(
                name: "SipAccountSipLine");

            migrationBuilder.DropTable(
                name: "SipLines");

            migrationBuilder.DropIndex(
                name: "IX_SipAccounts_DefaultLineId",
                table: "SipAccounts");

            migrationBuilder.DropColumn(
                name: "DefaultLineId",
                table: "SipAccounts");

            migrationBuilder.DropColumn(
                name: "AutoSelectLine",
                table: "BatchCallJobs");

            migrationBuilder.DropColumn(
                name: "SelectedLineId",
                table: "BatchCallJobs");
        }
    }
}
