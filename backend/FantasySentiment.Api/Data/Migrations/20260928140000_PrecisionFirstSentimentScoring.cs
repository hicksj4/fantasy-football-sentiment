using FantasySentiment.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasySentiment.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FantasySentimentDbContext))]
    [Migration("20260928140000_PrecisionFirstSentimentScoring")]
    public partial class PrecisionFirstSentimentScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnalysisDisposition",
                table: "PlayerMentions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Scored");

            migrationBuilder.AddColumn<string>(
                name: "AnalysisDispositionReason",
                table: "PlayerMentions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Scored");

            migrationBuilder.AddColumn<double>(
                name: "SemanticConfidence",
                table: "PlayerMentions",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<bool>(
                name: "HasScoredSentiment",
                table: "PlayerSnapshots",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MentionOnlyCount",
                table: "PlayerSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScoredMentionCount",
                table: "PlayerSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "SentimentCoverage",
                table: "PlayerSnapshots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "HasScoredSentiment",
                table: "SnapshotPlayerAggregates",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MentionOnlyCount",
                table: "SnapshotPlayerAggregates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScoredMentionCount",
                table: "SnapshotPlayerAggregates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "SentimentCoverage",
                table: "SnapshotPlayerAggregates",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.Sql(
                """
                UPDATE "PlayerSnapshots"
                SET "ScoredMentionCount" = "MentionCount",
                    "MentionOnlyCount" = 0,
                    "SentimentCoverage" = CASE WHEN "MentionCount" > 0 THEN 1.0 ELSE 0.0 END,
                    "HasScoredSentiment" = "MentionCount" > 0;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "SnapshotPlayerAggregates"
                SET "ScoredMentionCount" = "MentionCount",
                    "MentionOnlyCount" = 0,
                    "SentimentCoverage" = CASE WHEN "MentionCount" > 0 THEN 1.0 ELSE 0.0 END,
                    "HasScoredSentiment" = "MentionCount" > 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMentions_AnalysisDisposition",
                table: "PlayerMentions",
                column: "AnalysisDisposition");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerMentions_AnalysisDisposition",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "HasScoredSentiment",
                table: "SnapshotPlayerAggregates");

            migrationBuilder.DropColumn(
                name: "MentionOnlyCount",
                table: "SnapshotPlayerAggregates");

            migrationBuilder.DropColumn(
                name: "ScoredMentionCount",
                table: "SnapshotPlayerAggregates");

            migrationBuilder.DropColumn(
                name: "SentimentCoverage",
                table: "SnapshotPlayerAggregates");

            migrationBuilder.DropColumn(
                name: "HasScoredSentiment",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "MentionOnlyCount",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "ScoredMentionCount",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "SentimentCoverage",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "AnalysisDisposition",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "AnalysisDispositionReason",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "SemanticConfidence",
                table: "PlayerMentions");
        }
    }
}
