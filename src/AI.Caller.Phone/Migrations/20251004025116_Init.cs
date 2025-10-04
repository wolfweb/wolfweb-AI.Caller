using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Recordings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SipUsername = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recordings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SipAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SipUsername = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SipPassword = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SipServer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SipAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TtsTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false, comment: "支持模板语言（如Liquid）的字符串, e.g., '您好{% if gender == 'Male' %}先生{% else %}女士{% endif %}。'"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlayCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HangupAfterPlay = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TtsVariables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false, comment: "在模板中使用的占位符，不含大括号, e.g., 'CustomerName'"),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsVariables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Password = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    Bio = table.Column<string>(type: "TEXT", nullable: true),
                    AutoRecording = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableAI = table.Column<bool>(type: "INTEGER", nullable: false),
                    SipAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    SipRegistered = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_SipAccounts_SipAccountId",
                        column: x => x.SipAccountId,
                        principalTable: "SipAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TtsTemplateVariable",
                columns: table => new
                {
                    TtsTemplatesId = table.Column<int>(type: "INTEGER", nullable: false),
                    VariablesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsTemplateVariable", x => new { x.TtsTemplatesId, x.VariablesId });
                    table.ForeignKey(
                        name: "FK_TtsTemplateVariable_TtsTemplates_TtsTemplatesId",
                        column: x => x.TtsTemplatesId,
                        principalTable: "TtsTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TtsTemplateVariable_TtsVariables_VariablesId",
                        column: x => x.VariablesId,
                        principalTable: "TtsVariables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserId1 = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contacts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Contacts_Users_UserId1",
                        column: x => x.UserId1,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Bio", "DisplayName", "Email", "EnableAI", "IsAdmin", "Password", "PhoneNumber", "RegisteredAt", "SipAccountId", "SipRegistered", "Username" },
                values: new object[] { 1, null, null, null, false, false, "password123", null, null, null, false, "admin" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_PhoneNumber",
                table: "Contacts",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_UserId",
                table: "Contacts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_UserId1",
                table: "Contacts",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_StartTime",
                table: "Recordings",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_Status",
                table: "Recordings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_UserId",
                table: "Recordings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SipAccounts_IsActive",
                table: "SipAccounts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SipAccounts_SipUsername",
                table: "SipAccounts",
                column: "SipUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TtsTemplateVariable_VariablesId",
                table: "TtsTemplateVariable",
                column: "VariablesId");

            migrationBuilder.CreateIndex(
                name: "IX_TtsVariables_Name",
                table: "TtsVariables",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_SipAccountId",
                table: "Users",
                column: "SipAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_SipRegistered",
                table: "Users",
                column: "SipRegistered");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "Recordings");

            migrationBuilder.DropTable(
                name: "TtsTemplateVariable");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "TtsTemplates");

            migrationBuilder.DropTable(
                name: "TtsVariables");

            migrationBuilder.DropTable(
                name: "SipAccounts");
        }
    }
}
