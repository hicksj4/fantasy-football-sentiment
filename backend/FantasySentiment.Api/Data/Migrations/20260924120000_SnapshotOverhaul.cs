using System;
using FantasySentiment.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FantasySentiment.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FantasySentimentDbContext))]
    [Migration("20260924120000_SnapshotOverhaul")]
    public partial class SnapshotOverhaul : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    Week = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalPosts = table.Column<int>(type: "integer", nullable: false),
                    TotalComments = table.Column<int>(type: "integer", nullable: false),
                    TotalPlayerMentions = table.Column<int>(type: "integer", nullable: false),
                    UniquePlayersMentioned = table.Column<int>(type: "integer", nullable: false),
                    ProcessingDurationMs = table.Column<double>(type: "double precision", nullable: false),
                    CommentsPerSecond = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisSnapshots", x => x.Id);
                });

            migrationBuilder.AddColumn<int>(
                name: "SnapshotId",
                table: "RedditPosts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotId",
                table: "PlayerMentions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SnapshotPlayerAggregates",
                columns: table => new
                {
                    SnapshotId = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    MentionCount = table.Column<int>(type: "integer", nullable: false),
                    PositiveCount = table.Column<int>(type: "integer", nullable: false),
                    NegativeCount = table.Column<int>(type: "integer", nullable: false),
                    NeutralCount = table.Column<int>(type: "integer", nullable: false),
                    RawSentimentScore = table.Column<double>(type: "double precision", nullable: false),
                    SentimentScore = table.Column<double>(type: "double precision", nullable: false),
                    CommunitySentimentScore = table.Column<double>(type: "double precision", nullable: false),
                    TotalRedditScore = table.Column<int>(type: "integer", nullable: false),
                    TotalVoteWeight = table.Column<double>(type: "double precision", nullable: false),
                    AverageVoteWeight = table.Column<double>(type: "double precision", nullable: false),
                    SampleStrengthScore = table.Column<double>(type: "double precision", nullable: false),
                    Confidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AddCount = table.Column<int>(type: "integer", nullable: false),
                    DropCount = table.Column<int>(type: "integer", nullable: false),
                    StartCount = table.Column<int>(type: "integer", nullable: false),
                    SitCount = table.Column<int>(type: "integer", nullable: false),
                    BuyCount = table.Column<int>(type: "integer", nullable: false),
                    SellCount = table.Column<int>(type: "integer", nullable: false),
                    HoldCount = table.Column<int>(type: "integer", nullable: false),
                    InjuryCount = table.Column<int>(type: "integer", nullable: false),
                    HypeCount = table.Column<int>(type: "integer", nullable: false),
                    PanicCount = table.Column<int>(type: "integer", nullable: false),
                    DiscussionShare = table.Column<double>(type: "double precision", nullable: false),
                    PositiveImpact = table.Column<double>(type: "double precision", nullable: false),
                    NegativeImpact = table.Column<double>(type: "double precision", nullable: false),
                    NetImpact = table.Column<double>(type: "double precision", nullable: false),
                    ImpactScore = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotPlayerAggregates", x => new { x.SnapshotId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_SnapshotPlayerAggregates_AnalysisSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "AnalysisSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SnapshotPlayerAggregates_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RedditPosts_SnapshotId",
                table: "RedditPosts",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMentions_SnapshotId",
                table: "PlayerMentions",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisSnapshots_Key",
                table: "AnalysisSnapshots",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisSnapshots_ProcessedAt",
                table: "AnalysisSnapshots",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisSnapshots_Season_Week",
                table: "AnalysisSnapshots",
                columns: new[] { "Season", "Week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotPlayerAggregates_NegativeImpact",
                table: "SnapshotPlayerAggregates",
                column: "NegativeImpact");

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotPlayerAggregates_PlayerId",
                table: "SnapshotPlayerAggregates",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotPlayerAggregates_PositiveImpact",
                table: "SnapshotPlayerAggregates",
                column: "PositiveImpact");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerMentions_AnalysisSnapshots_SnapshotId",
                table: "PlayerMentions",
                column: "SnapshotId",
                principalTable: "AnalysisSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RedditPosts_AnalysisSnapshots_SnapshotId",
                table: "RedditPosts",
                column: "SnapshotId",
                principalTable: "AnalysisSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerMentions_AnalysisSnapshots_SnapshotId",
                table: "PlayerMentions");

            migrationBuilder.DropForeignKey(
                name: "FK_RedditPosts_AnalysisSnapshots_SnapshotId",
                table: "RedditPosts");

            migrationBuilder.DropTable(
                name: "SnapshotPlayerAggregates");

            migrationBuilder.DropTable(
                name: "AnalysisSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_RedditPosts_SnapshotId",
                table: "RedditPosts");

            migrationBuilder.DropIndex(
                name: "IX_PlayerMentions_SnapshotId",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "SnapshotId",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "SnapshotId",
                table: "PlayerMentions");
        }
    }
}
