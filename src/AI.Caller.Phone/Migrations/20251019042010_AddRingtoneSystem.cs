using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class AddRingtoneSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ringtones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Duration = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    UploadedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ringtones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ringtones_Users_UploadedBy",
                        column: x => x.UploadedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemRingtoneSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DefaultIncomingRingtoneId = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultRingbackToneId = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemRingtoneSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemRingtoneSettings_Ringtones_DefaultIncomingRingtoneId",
                        column: x => x.DefaultIncomingRingtoneId,
                        principalTable: "Ringtones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SystemRingtoneSettings_Ringtones_DefaultRingbackToneId",
                        column: x => x.DefaultRingbackToneId,
                        principalTable: "Ringtones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SystemRingtoneSettings_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserRingtoneSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    IncomingRingtoneId = table.Column<int>(type: "INTEGER", nullable: true),
                    RingbackToneId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRingtoneSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRingtoneSettings_Ringtones_IncomingRingtoneId",
                        column: x => x.IncomingRingtoneId,
                        principalTable: "Ringtones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserRingtoneSettings_Ringtones_RingbackToneId",
                        column: x => x.RingbackToneId,
                        principalTable: "Ringtones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserRingtoneSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ringtones_IsSystem",
                table: "Ringtones",
                column: "IsSystem");

            migrationBuilder.CreateIndex(
                name: "IX_Ringtones_Type",
                table: "Ringtones",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Ringtones_UploadedBy",
                table: "Ringtones",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SystemRingtoneSettings_DefaultIncomingRingtoneId",
                table: "SystemRingtoneSettings",
                column: "DefaultIncomingRingtoneId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemRingtoneSettings_DefaultRingbackToneId",
                table: "SystemRingtoneSettings",
                column: "DefaultRingbackToneId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemRingtoneSettings_UpdatedBy",
                table: "SystemRingtoneSettings",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserRingtoneSettings_IncomingRingtoneId",
                table: "UserRingtoneSettings",
                column: "IncomingRingtoneId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRingtoneSettings_RingbackToneId",
                table: "UserRingtoneSettings",
                column: "RingbackToneId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRingtoneSettings_UserId",
                table: "UserRingtoneSettings",
                column: "UserId",
                unique: true);

            // 插入系统内置铃音
            migrationBuilder.InsertData(
                table: "Ringtones",
                columns: new[] { "Name", "FileName", "FilePath", "FileSize", "Duration", "Type", "IsSystem", "UploadedBy", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { "默认铃音", "default.mp3", "/ringtones/default.mp3", 102400L, 10, "Both", true, null, DateTime.UtcNow, DateTime.UtcNow }
                });

            // 初始化系统配置（默认使用 ID=1 的铃音）
            migrationBuilder.InsertData(
                table: "SystemRingtoneSettings",
                columns: new[] { "Id", "DefaultIncomingRingtoneId", "DefaultRingbackToneId", "UpdatedBy", "UpdatedAt" },
                values: new object[] { 1, 1, 1, null, DateTime.UtcNow });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemRingtoneSettings");

            migrationBuilder.DropTable(
                name: "UserRingtoneSettings");

            migrationBuilder.DropTable(
                name: "Ringtones");
        }
    }
}
