using Microsoft.EntityFrameworkCore;
using AfterTheCredits.Models;
using AfterTheCredits.Services;

namespace AfterTheCredits.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options, DemoVisitorSession session)
        : base(options)
    {
        _session = session;
    }

    private readonly DemoVisitorSession _session;

    public DbSet<DemoVisitor> DemoVisitors { get; set; } = null!;

    public DbSet<WatchlistEntry> WatchlistEntries { get; set; } = null!;

    public DbSet<MovieEmbedding> MovieEmbeddings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MovieEmbedding>().HasKey(embedding => embedding.CacheKey);
        modelBuilder.Entity<DemoVisitor>().HasIndex(visitor => visitor.ExpiresAtUtc);

        var watchlist = modelBuilder.Entity<WatchlistEntry>();
        watchlist.HasQueryFilter(entry => (Guid?)entry.VisitorId == _session.VisitorId);
        watchlist.HasIndex(entry => new { entry.VisitorId, entry.MovieId }).IsUnique();
        watchlist.HasOne<DemoVisitor>().WithMany().HasForeignKey(entry => entry.VisitorId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public Task<int> SaveWatchlistMovieAsync(int movieId, CancellationToken cancellationToken,
        bool? isWatched = null, double? myRating = null, bool keepHigherRating = false)
    {
        var visitorId = _session.VisitorId
            ?? throw new InvalidOperationException("A demo visitor is required.");
        var now = DateTime.UtcNow;

        return Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WatchlistEntries" ("VisitorId", "MovieId", "IsWatched", "MyRating")
            SELECT "Id", {movieId}, {isWatched ?? false}, {myRating}
            FROM "DemoVisitors"
            WHERE "Id" = {visitorId} AND "ExpiresAtUtc" > {now}
            ON CONFLICT ("VisitorId", "MovieId") DO UPDATE SET
                "IsWatched" = COALESCE({isWatched}, "WatchlistEntries"."IsWatched"),
                "MyRating" = CASE
                    WHEN {keepHigherRating} AND "WatchlistEntries"."MyRating" > {myRating}
                        THEN "WatchlistEntries"."MyRating"
                    ELSE COALESCE({myRating}, "WatchlistEntries"."MyRating")
                END
            """, cancellationToken);
    }
}
