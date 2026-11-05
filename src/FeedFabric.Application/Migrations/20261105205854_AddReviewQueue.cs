using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeedFabric.Application.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Everything already saved was visible, and every moderation event so far was a block
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Posts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Visible");

            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "ModerationEvents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "blocked");

            migrationBuilder.AddColumn<Guid>(
                name: "CommentId",
                table: "ModerationEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Decision",
                table: "ModerationEvents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PostId",
                table: "ModerationEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "ModerationEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "ModerationEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Comments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Visible");

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reports_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reports_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reports_Users_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationEvents_Open",
                table: "ModerationEvents",
                column: "CreatedAt",
                filter: "\"ReviewedAt\" IS NULL AND \"Action\" <> 'blocked'");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationEvents_PostId_CommentId",
                table: "ModerationEvents",
                columns: new[] { "PostId", "CommentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_CommentId",
                table: "Reports",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_PostId_CommentId_ReporterId",
                table: "Reports",
                columns: new[] { "PostId", "CommentId", "ReporterId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReporterId",
                table: "Reports",
                column: "ReporterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_ModerationEvents_Open",
                table: "ModerationEvents");

            migrationBuilder.DropIndex(
                name: "IX_ModerationEvents_PostId_CommentId",
                table: "ModerationEvents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "Action",
                table: "ModerationEvents");

            migrationBuilder.DropColumn(
                name: "CommentId",
                table: "ModerationEvents");

            migrationBuilder.DropColumn(
                name: "Decision",
                table: "ModerationEvents");

            migrationBuilder.DropColumn(
                name: "PostId",
                table: "ModerationEvents");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "ModerationEvents");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "ModerationEvents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Comments");
        }
    }
}
