using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Caller.Phone.Migrations
{
    /// <inheritdoc />
    public partial class AddScenarioToBatchJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TtsTemplateId",
                table: "BatchCallJobs",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "ScenarioRecordingId",
                table: "BatchCallJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchCallJobs_ScenarioRecordingId",
                table: "BatchCallJobs",
                column: "ScenarioRecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchCallJobs_TtsTemplateId",
                table: "BatchCallJobs",
                column: "TtsTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_BatchCallJobs_ScenarioRecordings_ScenarioRecordingId",
                table: "BatchCallJobs",
                column: "ScenarioRecordingId",
                principalTable: "ScenarioRecordings",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BatchCallJobs_TtsTemplates_TtsTemplateId",
                table: "BatchCallJobs",
                column: "TtsTemplateId",
                principalTable: "TtsTemplates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BatchCallJobs_ScenarioRecordings_ScenarioRecordingId",
                table: "BatchCallJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_BatchCallJobs_TtsTemplates_TtsTemplateId",
                table: "BatchCallJobs");

            migrationBuilder.DropIndex(
                name: "IX_BatchCallJobs_ScenarioRecordingId",
                table: "BatchCallJobs");

            migrationBuilder.DropIndex(
                name: "IX_BatchCallJobs_TtsTemplateId",
                table: "BatchCallJobs");

            migrationBuilder.DropColumn(
                name: "ScenarioRecordingId",
                table: "BatchCallJobs");

            migrationBuilder.AlterColumn<int>(
                name: "TtsTemplateId",
                table: "BatchCallJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
