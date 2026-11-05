using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeedFabric.Application.Migrations
{
    /// <inheritdoc />
    public partial class AddTagsAndMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Mentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MentionedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mentions_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mentions_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mentions_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mentions_Users_MentionedUserId",
                        column: x => x.MentionedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PostTags",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostTags", x => new { x.PostId, x.Tag });
                    table.ForeignKey(
                        name: "FK_PostTags_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username");

            migrationBuilder.CreateIndex(
                name: "IX_Mentions_AuthorId",
                table: "Mentions",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Mentions_CommentId",
                table: "Mentions",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Mentions_MentionedUserId_Id",
                table: "Mentions",
                columns: new[] { "MentionedUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Mentions_PostId_CommentId",
                table: "Mentions",
                columns: new[] { "PostId", "CommentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostTags_CreatedAt",
                table: "PostTags",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PostTags_Tag_PostId",
                table: "PostTags",
                columns: new[] { "Tag", "PostId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Mentions");

            migrationBuilder.DropTable(
                name: "PostTags");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");
        }
    }
}
