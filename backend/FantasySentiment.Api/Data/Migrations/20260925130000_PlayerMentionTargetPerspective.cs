using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasySentiment.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FantasySentimentDbContext))]
    [Migration("20260925130000_PlayerMentionTargetPerspective")]
    public partial class PlayerMentionTargetPerspective : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetPerspective",
                table: "PlayerMentions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: TargetPerspectives.Unknown);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetPerspective",
                table: "PlayerMentions");
        }
    }
}
