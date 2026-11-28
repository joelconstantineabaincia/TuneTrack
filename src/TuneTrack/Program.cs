using Microsoft.EntityFrameworkCore;
using TuneTrack.Components;
using TuneTrack.Data;
using TuneTrack.Services.Analysis;
using TuneTrack.Services.Providers;
using TuneTrack.Services.Recommendations;
using TuneTrack.Services.Samples;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// HTTP Client & Memory Cache
builder.Services.AddHttpClient("MusicApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TuneTrack/1.0 (Educational Music Analytics)");
});
builder.Services.AddMemoryCache();

// Core TuneTrack Domain Services
builder.Services.AddSingleton<IDiversityScorer, DiversityScorer>();
builder.Services.AddSingleton<ISamplePlaylistService, SamplePlaylistService>();
builder.Services.AddSingleton<ITrackEnricherService, TrackEnricherService>();
builder.Services.AddSingleton<IRecommendationEngine, RecommendationEngine>();

// Modular Playlist Providers
builder.Services.AddSingleton<IPlaylistProvider, SpotifyPlaylistProvider>();
builder.Services.AddSingleton<IPlaylistProvider, AppleMusicPlaylistProvider>();
builder.Services.AddSingleton<IPlaylistProvider, YouTubeMusicPlaylistProvider>();

// Primary Application Service
builder.Services.AddScoped<IPlaylistService, PlaylistService>();

// Configure AppDbContext (PostgreSQL with In-Memory fallback)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
bool isPlaceholder = string.IsNullOrWhiteSpace(connectionString)
    || connectionString.Contains("YOUR_PASSWORD", StringComparison.OrdinalIgnoreCase)
    || connectionString.Contains("[PASSWORD]", StringComparison.OrdinalIgnoreCase)
    || connectionString.Contains("changeme", StringComparison.OrdinalIgnoreCase)
    || connectionString.Contains("<password>", StringComparison.OrdinalIgnoreCase);

if (isPlaceholder)
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseInMemoryDatabase("TuneTrackInMemory"));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString, npgsqlOptions =>
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null)));
}

var app = builder.Build();

// Log warning at startup if using fallback mode
if (isPlaceholder)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning("Supabase PostgreSQL connection string is missing or contains placeholder credentials. Running in EF Core In-Memory fallback mode.");
}
else
{
    // Apply migrations if using PostgreSQL (do not auto-apply in fallback mode)
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Failed to apply database migrations on startup.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseStaticFiles();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
