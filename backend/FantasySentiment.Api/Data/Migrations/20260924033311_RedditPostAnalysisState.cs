using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasySentiment.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RedditPostAnalysisState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnalysisStatus",
                table: "RedditPosts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Discovered");

            migrationBuilder.AddColumn<string>(
                name: "AnalysisStatusReason",
                table: "RedditPosts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAnalyzedAt",
                table: "RedditPosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastAnalyzedCommentCount",
                table: "RedditPosts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastObservedAt",
                table: "RedditPosts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastObservedCommentCountAt",
                table: "RedditPosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostType",
                table: "RedditPosts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<int>(
                name: "PreviousCommentCount",
                table: "RedditPosts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "RedditPosts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "RedditPosts"
                SET "AnalysisStatus" = 'Discovered',
                    "PostType" = 'Normal',
                    "LastObservedAt" = "CreatedAt",
                    "PreviousCommentCount" = "CommentCount",
                    "AnalysisStatusReason" = 'existing post carried forward before selective analysis'
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RedditPosts_AnalysisStatus",
                table: "RedditPosts",
                column: "AnalysisStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RedditPosts_AnalysisStatus",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "AnalysisStatus",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "AnalysisStatusReason",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "LastAnalyzedAt",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "LastAnalyzedCommentCount",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "LastObservedAt",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "LastObservedCommentCountAt",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "PostType",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "PreviousCommentCount",
                table: "RedditPosts");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "RedditPosts");
        }
    }
}
