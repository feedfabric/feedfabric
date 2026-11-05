using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeedFabric.Application.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationAndRankingSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Explored",
                table: "FeedLog",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotInterested",
                table: "FeedLog",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "Propensity",
                table: "FeedLog",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "ModerationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Excerpt = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationEvents_CreatedAt",
                table: "ModerationEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationEvents_UserId_CreatedAt",
                table: "ModerationEvents",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModerationEvents");

            migrationBuilder.DropColumn(
                name: "Explored",
                table: "FeedLog");

            migrationBuilder.DropColumn(
                name: "NotInterested",
                table: "FeedLog");

            migrationBuilder.DropColumn(
                name: "Propensity",
                table: "FeedLog");
        }
    }
}
