using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeedFabric.Application.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Feed = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Ranker = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Features = table.Column<string>(type: "jsonb", nullable: false),
                    ServedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Seen = table.Column<bool>(type: "boolean", nullable: false),
                    Opened = table.Column<bool>(type: "boolean", nullable: false),
                    Liked = table.Column<bool>(type: "boolean", nullable: false),
                    Commented = table.Column<bool>(type: "boolean", nullable: false),
                    DwellMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedLog_ServedAt",
                table: "FeedLog",
                column: "ServedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FeedLog_ViewerId_PostId_ServedAt",
                table: "FeedLog",
                columns: new[] { "ViewerId", "PostId", "ServedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedLog");
        }
    }
}
