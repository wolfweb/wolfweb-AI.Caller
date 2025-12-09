using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class ExtendCallLogForAllScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ResolvedContent",
                table: "CallLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "CallLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "CallId",
                table: "CallLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CallScenario",
                table: "CallLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalleeNumber",
                table: "CallLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CalleeUserId",
                table: "CallLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CallerNumber",
                table: "CallLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CallerUserId",
                table: "CallLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "CallLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "Duration",
                table: "CallLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndTime",
                table: "CallLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinishStatus",
                table: "CallLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordingFilePath",
                table: "CallLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartTime",
                table: "CallLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_CalleeUserId",
                table: "CallLogs",
                column: "CalleeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_CallerUserId",
                table: "CallLogs",
                column: "CallerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_CallId",
                table: "CallLogs",
                column: "CallId");

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_CallScenario",
                table: "CallLogs",
                column: "CallScenario");

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_Direction",
                table: "CallLogs",
                column: "Direction");

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_StartTime",
                table: "CallLogs",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_Status",
                table: "CallLogs",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_CallLogs_Users_CalleeUserId",
                table: "CallLogs",
                column: "CalleeUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CallLogs_Users_CallerUserId",
                table: "CallLogs",
                column: "CallerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CallLogs_Users_CalleeUserId",
                table: "CallLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_CallLogs_Users_CallerUserId",
                table: "CallLogs");

            migrationBuilder.DropIndex(
                name: "IX_CallLogs_CalleeUserId",
                table: "CallLogs");

            migrationBuilder.DropIndex(
                name: "IX_CallLogs_CallerUserId",
                table: "CallLogs");

            migrationBuilder.DropIndex(
                name: "IX_CallLogs_CallId",
                table: "CallLogs");

            migrationBuilder.DropIndex(
                name: "IX_CallLogs_CallScenario",
                table: "CallLogs");

            migrationBuilder.DropIndex(
                name: "IX_CallLogs_Direction",
                table: "CallLogs");

            migrationBuilder.DropIndex(
                name: "IX_CallLogs_StartTime",
                table: "CallLogs");

            migrationBuilder.DropIndex(
                name: "IX_CallLogs_Status",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "CallId",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "CallScenario",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "CalleeNumber",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "CalleeUserId",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "CallerNumber",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "CallerUserId",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "FinishStatus",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "RecordingFilePath",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "CallLogs");

            migrationBuilder.AlterColumn<string>(
                name: "ResolvedContent",
                table: "CallLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "CallLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
