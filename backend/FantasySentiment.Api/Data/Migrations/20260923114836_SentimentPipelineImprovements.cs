using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasySentiment.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SentimentPipelineImprovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AverageVoteWeight",
                table: "PlayerSnapshots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "CommunitySentimentScore",
                table: "PlayerSnapshots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Confidence",
                table: "PlayerSnapshots",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Low");

            migrationBuilder.AddColumn<double>(
                name: "RawSentimentScore",
                table: "PlayerSnapshots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "SampleStrengthScore",
                table: "PlayerSnapshots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "TotalRedditScore",
                table: "PlayerSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "TotalVoteWeight",
                table: "PlayerSnapshots",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "FantasyAdjustment",
                table: "PlayerMentions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "FinalSentimentScore",
                table: "PlayerMentions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "MatchedAlias",
                table: "PlayerMentions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "ModelSentimentScore",
                table: "PlayerMentions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RawSentimentScore",
                table: "PlayerMentions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "RedditScore",
                table: "PlayerMentions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "RuleBasedSentimentScore",
                table: "PlayerMentions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "SentimentAnalyzer",
                table: "PlayerMentions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "LegacyRuleBased");

            migrationBuilder.AddColumn<string>(
                name: "SentimentContext",
                table: "PlayerMentions",
                type: "character varying(1200)",
                maxLength: 1200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SentimentDiagnostics",
                table: "PlayerMentions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VoteWeight",
                table: "PlayerMentions",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<double>(
                name: "WeightedSentimentContribution",
                table: "PlayerMentions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.Sql(
                """
                UPDATE "PlayerMentions" AS pm
                SET
                    "RawSentimentScore" = pm."SentimentScore",
                    "FinalSentimentScore" = pm."SentimentScore",
                    "RuleBasedSentimentScore" = pm."SentimentScore",
                    "SentimentAnalyzer" = 'LegacyRuleBased',
                    "SentimentContext" = LEFT(rc."Body", 1200),
                    "RedditScore" = rc."Score",
                    "VoteWeight" =
                        CASE
                            WHEN rc."Score" = 0 THEN 1.0
                            WHEN rc."Score" < 0 THEN ROUND(GREATEST(0.25, LEAST(1.0, 1.0 / (1.0 + LN(1 + ABS(rc."Score")) / 2.5)))::numeric, 3)::double precision
                            ELSE ROUND(GREATEST(1.0, LEAST(4.0, 1.0 + LN(1 + rc."Score") / 2.5))::numeric, 3)::double precision
                        END,
                    "WeightedSentimentContribution" = ROUND((
                        pm."SentimentScore" *
                        CASE
                            WHEN rc."Score" = 0 THEN 1.0
                            WHEN rc."Score" < 0 THEN ROUND(GREATEST(0.25, LEAST(1.0, 1.0 / (1.0 + LN(1 + ABS(rc."Score")) / 2.5)))::numeric, 3)::double precision
                            ELSE ROUND(GREATEST(1.0, LEAST(4.0, 1.0 + LN(1 + rc."Score") / 2.5))::numeric, 3)::double precision
                        END
                    )::numeric, 3)::double precision
                FROM "RedditComments" AS rc
                WHERE pm."CommentId" = rc."Id";
                """);

            migrationBuilder.Sql(
                """
                UPDATE "PlayerSnapshots"
                SET
                    "RawSentimentScore" = "SentimentScore",
                    "CommunitySentimentScore" = "SentimentScore",
                    "TotalVoteWeight" = "MentionCount",
                    "AverageVoteWeight" = CASE WHEN "MentionCount" = 0 THEN 0.0 ELSE 1.0 END,
                    "SampleStrengthScore" = ROUND(LEAST(1.0, ("MentionCount" / 50.0) * 0.6 + ("MentionCount" / 100.0) * 0.4)::numeric, 3)::double precision,
                    "Confidence" =
                        CASE
                            WHEN "MentionCount" >= 25 THEN 'High'
                            WHEN "MentionCount" >= 6 THEN 'Medium'
                            ELSE 'Low'
                        END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageVoteWeight",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "CommunitySentimentScore",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "RawSentimentScore",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "SampleStrengthScore",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "TotalRedditScore",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "TotalVoteWeight",
                table: "PlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "FantasyAdjustment",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "FinalSentimentScore",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "MatchedAlias",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "ModelSentimentScore",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "RawSentimentScore",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "RedditScore",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "RuleBasedSentimentScore",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "SentimentAnalyzer",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "SentimentContext",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "SentimentDiagnostics",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "VoteWeight",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "WeightedSentimentContribution",
                table: "PlayerMentions");
        }
    }
}
