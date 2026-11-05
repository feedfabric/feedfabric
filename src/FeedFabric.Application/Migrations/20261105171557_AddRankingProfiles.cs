using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeedFabric.Application.Migrations
{
    /// <inheritdoc />
    public partial class AddRankingProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Profile",
                table: "FeedLog",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RankingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Settings = table.Column<string>(type: "jsonb", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SavedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankingProfiles_Key_Version",
                table: "RankingProfiles",
                columns: new[] { "Key", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankingProfiles");

            migrationBuilder.DropColumn(
                name: "Profile",
                table: "FeedLog");
        }
    }
}
