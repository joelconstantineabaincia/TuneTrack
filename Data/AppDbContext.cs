namespace TuneTrack.Data;

using Microsoft.EntityFrameworkCore;
using TuneTrack.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AnalyzedPlaylist> AnalyzedPlaylists => Set<AnalyzedPlaylist>();
    public DbSet<AnalyzedTrack> AnalyzedTracks => Set<AnalyzedTrack>();
    public DbSet<SavedRecommendation> SavedRecommendations => Set<SavedRecommendation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AnalyzedPlaylist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientId);
            entity.Property(e => e.ClientId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Platform).IsRequired().HasMaxLength(64);
            entity.Property(e => e.SourceUrl).IsRequired().HasMaxLength(2048);

            entity.HasMany(e => e.Tracks)
                  .WithOne(t => t.Playlist)
                  .HasForeignKey(t => t.PlaylistId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnalyzedTrack>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Artist).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Album).HasMaxLength(256);
            entity.Property(e => e.Genre).HasMaxLength(128);
            entity.Property(e => e.ArtworkUrl).HasMaxLength(2048);
            entity.Property(e => e.PreviewUrl).HasMaxLength(2048);
        });

        modelBuilder.Entity<SavedRecommendation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientId);
            entity.Property(e => e.ClientId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Artist).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Genre).HasMaxLength(128);
            entity.Property(e => e.ArtworkUrl).HasMaxLength(2048);
        });
    }
}
