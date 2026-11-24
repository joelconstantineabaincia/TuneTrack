using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TuneTrack.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyzedPlaylists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CoverUrl = table.Column<string>(type: "text", nullable: true),
                    DiversityScore = table.Column<double>(type: "double precision", nullable: false),
                    GenreVariety = table.Column<double>(type: "double precision", nullable: false),
                    ArtistUniqueness = table.Column<double>(type: "double precision", nullable: false),
                    EraSpread = table.Column<double>(type: "double precision", nullable: false),
                    TrackCount = table.Column<int>(type: "integer", nullable: false),
                    TotalDuration = table.Column<string>(type: "text", nullable: false),
                    AnalyzedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyzedPlaylists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedRecommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Artist = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Genre = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ArtworkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SourcePlaylistId = table.Column<int>(type: "integer", nullable: true),
                    SavedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedRecommendations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalyzedTracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlaylistId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Artist = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Album = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Genre = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReleaseYear = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    ArtworkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PreviewUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyzedTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalyzedTracks_AnalyzedPlaylists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "AnalyzedPlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyzedPlaylists_ClientId",
                table: "AnalyzedPlaylists",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyzedTracks_PlaylistId",
                table: "AnalyzedTracks",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedRecommendations_ClientId",
                table: "SavedRecommendations",
                column: "ClientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyzedTracks");

            migrationBuilder.DropTable(
                name: "SavedRecommendations");

            migrationBuilder.DropTable(
                name: "AnalyzedPlaylists");
        }
    }
}
