using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class AddTtsCallDocumentAndInboundTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboundTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    WelcomeScript = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseRules = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TtsCallDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UploadTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalRecords = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsCallDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TtsCallRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    AddressTemplate = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TtsContent = table.Column<string>(type: "TEXT", nullable: false),
                    CallStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    CallTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsCallRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TtsCallRecords_TtsCallDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "TtsCallDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundTemplates_IsActive",
                table: "InboundTemplates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_InboundTemplates_IsDefault",
                table: "InboundTemplates",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_InboundTemplates_UserId",
                table: "InboundTemplates",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TtsCallDocuments_Status",
                table: "TtsCallDocuments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TtsCallDocuments_UploadTime",
                table: "TtsCallDocuments",
                column: "UploadTime");

            migrationBuilder.CreateIndex(
                name: "IX_TtsCallDocuments_UserId",
                table: "TtsCallDocuments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TtsCallRecords_CallStatus",
                table: "TtsCallRecords",
                column: "CallStatus");

            migrationBuilder.CreateIndex(
                name: "IX_TtsCallRecords_DocumentId",
                table: "TtsCallRecords",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TtsCallRecords_PhoneNumber",
                table: "TtsCallRecords",
                column: "PhoneNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundTemplates");

            migrationBuilder.DropTable(
                name: "TtsCallRecords");

            migrationBuilder.DropTable(
                name: "TtsCallDocuments");
        }
    }
}
