using FantasySentiment.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasySentiment.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FantasySentimentDbContext))]
    [Migration("20260928120000_PlayerMentionSemanticSets")]
    public partial class PlayerMentionSemanticSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscussionSignals",
                table: "PlayerMentions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FantasyActions",
                table: "PlayerMentions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscussionSignals",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "FantasyActions",
                table: "PlayerMentions");
        }
    }
}
