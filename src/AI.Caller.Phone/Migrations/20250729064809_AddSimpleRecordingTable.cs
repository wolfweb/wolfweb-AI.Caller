using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class AddSimpleRecordingTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallRecordings");

            migrationBuilder.DropTable(
                name: "RecordingSettings");

            migrationBuilder.AddColumn<bool>(
                name: "AutoRecording",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

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

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "AutoRecording",
                value: false);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Recordings");

            migrationBuilder.DropColumn(
                name: "AutoRecording",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "CallRecordings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AudioFormat = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CallId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CalleeNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CallerNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallRecordings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecordingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AudioFormat = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    AudioQuality = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoRecording = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EnableCompression = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxStorageSizeMB = table.Column<long>(type: "INTEGER", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordingSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallRecordings_CallId",
                table: "CallRecordings",
                column: "CallId");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecordings_StartTime",
                table: "CallRecordings",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecordings_Status",
                table: "CallRecordings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecordings_UserId",
                table: "CallRecordings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordingSettings_UserId",
                table: "RecordingSettings",
                column: "UserId",
                unique: true);
        }
    }
}
