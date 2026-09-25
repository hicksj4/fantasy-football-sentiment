using FantasySentiment.Api.Data;
using FantasySentiment.Api.Domain;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasySentiment.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FantasySentimentDbContext))]
    [Migration("20260925120000_PlayerMentionTargetAttribution")]
    public partial class PlayerMentionTargetAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetAttributionMethod",
                table: "PlayerMentions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: TargetAttributionMethods.Explicit);

            migrationBuilder.AddColumn<double>(
                name: "TargetConfidence",
                table: "PlayerMentions",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetAttributionMethod",
                table: "PlayerMentions");

            migrationBuilder.DropColumn(
                name: "TargetConfidence",
                table: "PlayerMentions");
        }
    }
}
