using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FantasySentiment.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Team = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Position = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RedditPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RedditId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subreddit = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Url = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    CommentCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedditPosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerAliases_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSnapshots",
                columns: table => new
                {
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MentionCount = table.Column<int>(type: "integer", nullable: false),
                    PositiveCount = table.Column<int>(type: "integer", nullable: false),
                    NegativeCount = table.Column<int>(type: "integer", nullable: false),
                    NeutralCount = table.Column<int>(type: "integer", nullable: false),
                    SentimentScore = table.Column<double>(type: "double precision", nullable: false),
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
                    BuzzScore = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSnapshots", x => new { x.PlayerId, x.Timestamp });
                    table.ForeignKey(
                        name: "FK_PlayerSnapshots_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RedditComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RedditId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    ParentRedditId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedditComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedditComments_RedditPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "RedditPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMentions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<int>(type: "integer", nullable: false),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    SentimentScore = table.Column<double>(type: "double precision", nullable: false),
                    SentimentCategory = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Intent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerMentions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerMentions_RedditComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "RedditComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAliases_PlayerId_Alias",
                table: "PlayerAliases",
                columns: new[] { "PlayerId", "Alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMentions_CommentId",
                table: "PlayerMentions",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMentions_CreatedAt",
                table: "PlayerMentions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMentions_PlayerId_CommentId",
                table: "PlayerMentions",
                columns: new[] { "PlayerId", "CommentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_Name",
                table: "Players",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Team_Position",
                table: "Players",
                columns: new[] { "Team", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSnapshots_Timestamp",
                table: "PlayerSnapshots",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_RedditComments_CreatedAt",
                table: "RedditComments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RedditComments_PostId",
                table: "RedditComments",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_RedditComments_RedditId",
                table: "RedditComments",
                column: "RedditId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RedditPosts_CreatedAt",
                table: "RedditPosts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RedditPosts_RedditId",
                table: "RedditPosts",
                column: "RedditId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerAliases");

            migrationBuilder.DropTable(
                name: "PlayerMentions");

            migrationBuilder.DropTable(
                name: "PlayerSnapshots");

            migrationBuilder.DropTable(
                name: "RedditComments");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "RedditPosts");
        }
    }
}
