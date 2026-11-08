using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeedFabric.Application.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TopicSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Topics = table.Column<string>(type: "jsonb", nullable: false),
                    Library = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SavedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SavedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicSets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TopicSets_Version",
                table: "TopicSets",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TopicSets");
        }
    }
}
