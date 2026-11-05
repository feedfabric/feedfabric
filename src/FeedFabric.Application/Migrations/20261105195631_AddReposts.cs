using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeedFabric.Application.Migrations
{
    /// <inheritdoc />
    public partial class AddReposts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RepostOfId",
                table: "Posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_RepostOfId_AuthorId",
                table: "Posts",
                columns: new[] { "RepostOfId", "AuthorId" },
                unique: true,
                filter: "\"RepostOfId\" IS NOT NULL AND \"DeletedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_Posts_RepostOfId",
                table: "Posts",
                column: "RepostOfId",
                principalTable: "Posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_Posts_RepostOfId",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Posts_RepostOfId_AuthorId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "RepostOfId",
                table: "Posts");
        }
    }
}
