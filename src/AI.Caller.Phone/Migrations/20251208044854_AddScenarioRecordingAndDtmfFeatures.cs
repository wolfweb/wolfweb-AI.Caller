using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class AddScenarioRecordingAndDtmfFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScenarioRecordingId",
                table: "CallLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DtmfInputTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InputType = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidatorType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MaxLength = table.Column<int>(type: "INTEGER", nullable: false),
                    MinLength = table.Column<int>(type: "INTEGER", nullable: false),
                    TerminationKey = table.Column<char>(type: "TEXT", nullable: false),
                    BackspaceKey = table.Column<char>(type: "TEXT", nullable: false),
                    PromptText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SuccessText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ErrorText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TimeoutText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MaxRetries = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DtmfInputTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CallId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MonitorUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    MonitorUserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    InterventionTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    InterventionReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CallLogId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitoringSessions_CallLogs_CallLogId",
                        column: x => x.CallLogId,
                        principalTable: "CallLogs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlaybackControls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CallId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CurrentSegmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    PlaybackState = table.Column<int>(type: "INTEGER", nullable: false),
                    LastInterventionSegmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    SkippedSegments = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PausedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResumedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    CallLogId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackControls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybackControls_CallLogs_CallLogId",
                        column: x => x.CallLogId,
                        principalTable: "CallLogs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScenarioRecordings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioRecordings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScenarioRecordingSegments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScenarioRecordingId = table.Column<int>(type: "INTEGER", nullable: false),
                    SegmentOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    SegmentType = table.Column<int>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TtsText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TtsVariables = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DtmfTemplateId = table.Column<int>(type: "INTEGER", nullable: true),
                    DtmfVariableName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ConditionExpression = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    NextSegmentIdOnTrue = table.Column<int>(type: "INTEGER", nullable: true),
                    NextSegmentIdOnFalse = table.Column<int>(type: "INTEGER", nullable: true),
                    Duration = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioRecordingSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScenarioRecordingSegments_DtmfInputTemplates_DtmfTemplateId",
                        column: x => x.DtmfTemplateId,
                        principalTable: "DtmfInputTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScenarioRecordingSegments_ScenarioRecordings_ScenarioRecordingId",
                        column: x => x.ScenarioRecordingId,
                        principalTable: "ScenarioRecordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DtmfInputRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CallId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SegmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: true),
                    InputValue = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValidationMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    InputTime = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Duration = table.Column<int>(type: "INTEGER", nullable: false),
                    CallLogId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DtmfInputRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DtmfInputRecords_CallLogs_CallLogId",
                        column: x => x.CallLogId,
                        principalTable: "CallLogs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DtmfInputRecords_DtmfInputTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "DtmfInputTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DtmfInputRecords_ScenarioRecordingSegments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "ScenarioRecordingSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_ScenarioRecordingId",
                table: "CallLogs",
                column: "ScenarioRecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_DtmfInputRecords_CallId",
                table: "DtmfInputRecords",
                column: "CallId");

            migrationBuilder.CreateIndex(
                name: "IX_DtmfInputRecords_CallLogId",
                table: "DtmfInputRecords",
                column: "CallLogId");

            migrationBuilder.CreateIndex(
                name: "IX_DtmfInputRecords_InputTime",
                table: "DtmfInputRecords",
                column: "InputTime");

            migrationBuilder.CreateIndex(
                name: "IX_DtmfInputRecords_SegmentId",
                table: "DtmfInputRecords",
                column: "SegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DtmfInputRecords_TemplateId",
                table: "DtmfInputRecords",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_DtmfInputTemplates_InputType",
                table: "DtmfInputTemplates",
                column: "InputType");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringSessions_CallId",
                table: "MonitoringSessions",
                column: "CallId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringSessions_CallLogId",
                table: "MonitoringSessions",
                column: "CallLogId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringSessions_IsActive",
                table: "MonitoringSessions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringSessions_MonitorUserId",
                table: "MonitoringSessions",
                column: "MonitorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackControls_CallId",
                table: "PlaybackControls",
                column: "CallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackControls_CallLogId",
                table: "PlaybackControls",
                column: "CallLogId");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioRecordings_Category",
                table: "ScenarioRecordings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioRecordings_IsActive",
                table: "ScenarioRecordings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioRecordingSegments_DtmfTemplateId",
                table: "ScenarioRecordingSegments",
                column: "DtmfTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioRecordingSegments_ScenarioRecordingId",
                table: "ScenarioRecordingSegments",
                column: "ScenarioRecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioRecordingSegments_SegmentOrder",
                table: "ScenarioRecordingSegments",
                column: "SegmentOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_CallLogs_ScenarioRecordings_ScenarioRecordingId",
                table: "CallLogs",
                column: "ScenarioRecordingId",
                principalTable: "ScenarioRecordings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CallLogs_ScenarioRecordings_ScenarioRecordingId",
                table: "CallLogs");

            migrationBuilder.DropTable(
                name: "DtmfInputRecords");

            migrationBuilder.DropTable(
                name: "MonitoringSessions");

            migrationBuilder.DropTable(
                name: "PlaybackControls");

            migrationBuilder.DropTable(
                name: "ScenarioRecordingSegments");

            migrationBuilder.DropTable(
                name: "DtmfInputTemplates");

            migrationBuilder.DropTable(
                name: "ScenarioRecordings");

            migrationBuilder.DropIndex(
                name: "IX_CallLogs_ScenarioRecordingId",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "ScenarioRecordingId",
                table: "CallLogs");
        }
    }
}
